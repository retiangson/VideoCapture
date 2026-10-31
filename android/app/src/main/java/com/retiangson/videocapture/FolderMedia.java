package com.retiangson.videocapture;

import android.content.ContentResolver;
import android.database.Cursor;
import android.net.Uri;
import android.os.CancellationSignal;
import android.provider.DocumentsContract;
import java.io.IOException;
import java.util.*;

/** Reads only a user-granted SAF tree. Bounded traversal never requests broad storage access. */
final class FolderMedia {
  record Entry(Uri uri, String name) {}

  record Result(List<Entry> entries, boolean limited, int skipped) {}

  private record Directory(String id, int depth) {}

  record Document(String id, String name, String mime) {}

  interface Source {
    List<Document> children(String id, CancellationSignal signal) throws IOException;

    Uri uri(String id);
  }

  static Result scan(ContentResolver resolver, Uri tree, CancellationSignal signal)
      throws IOException {
    return scan(
        new Source() {
          public Uri uri(String id) {
            return DocumentsContract.buildDocumentUriUsingTree(tree, id);
          }

          public List<Document> children(String id, CancellationSignal cancellation)
              throws IOException {
            String[] columns = {
              DocumentsContract.Document.COLUMN_DOCUMENT_ID,
              DocumentsContract.Document.COLUMN_DISPLAY_NAME,
              DocumentsContract.Document.COLUMN_MIME_TYPE
            };
            Uri children = DocumentsContract.buildChildDocumentsUriUsingTree(tree, id);
            List<Document> found = new ArrayList<>();
            try (Cursor cursor =
                resolver.query(children, columns, null, null, null, cancellation)) {
              if (cursor == null)
                throw new IOException("The document provider did not return folder contents.");
              while (cursor.moveToNext() && found.size() <= 10000) {
                cancellation.throwIfCanceled();
                found.add(
                    new Document(cursor.getString(0), cursor.getString(1), cursor.getString(2)));
              }
            }
            return found;
          }
        },
        DocumentsContract.getTreeDocumentId(tree),
        signal);
  }

  static Result scan(Source source, String root, CancellationSignal signal) throws IOException {
    ArrayDeque<Directory> pending = new ArrayDeque<>();
    pending.add(new Directory(root, 0));
    Set<String> visited = new HashSet<>();
    List<Entry> result = new ArrayList<>();
    int rows = 0, skipped = 0;
    boolean limited = false;
    while (!pending.isEmpty() && result.size() < 200 && rows < 10000 && visited.size() < 512) {
      signal.throwIfCanceled();
      Directory folder = pending.removeFirst();
      if (!visited.add(folder.id)) continue;
      List<Directory> children = new ArrayList<>();
      List<Entry> files = new ArrayList<>();
      try {
        for (Document document : source.children(folder.id, signal)) {
          signal.throwIfCanceled();
          if (++rows > 10000) {
            limited = true;
            break;
          }
          String id = document.id, name = document.name, mime = document.mime;
          if (id == null || name == null) {
            skipped++;
            continue;
          }
          if (DocumentsContract.Document.MIME_TYPE_DIR.equals(mime)) {
            if (folder.depth < 16) children.add(new Directory(id, folder.depth + 1));
            else limited = true;
          } else if (media(mime, name)) files.add(new Entry(source.uri(id), name));
        }
      } catch (SecurityException error) {
        if (visited.size() == 1) throw error;
        skipped++;
      }
      files.sort(
          Comparator.comparing(Entry::name, String.CASE_INSENSITIVE_ORDER)
              .thenComparing(entry -> entry.uri.toString()));
      for (Entry entry : files) {
        if (result.size() == 200) {
          limited = true;
          break;
        }
        result.add(entry);
      }
      children.sort(Comparator.comparing(Directory::id, String.CASE_INSENSITIVE_ORDER));
      pending.addAll(children);
    }
    limited |= !pending.isEmpty() || rows >= 10000;
    return new Result(List.copyOf(result), limited, skipped);
  }

  static boolean media(String mime, String name) {
    if (mime != null) mime = mime.toLowerCase(Locale.ROOT);
    if ("application/x-matroska".equals(mime) || "application/ogg".equals(mime)) return true;
    if (mime != null && (mime.startsWith("video/") || mime.startsWith("audio/"))) return true;
    if (mime != null && !mime.isEmpty() && !"application/octet-stream".equals(mime)) return false;
    String lower = name.toLowerCase(Locale.ROOT);
    return lower.matches(
        ".*\\.(mp4|mkv|webm|mov|avi|m4v|3gp|ts|m2ts|mpg|mpeg|mp3|m4a|aac|flac|wav|ogg|opus)$");
  }

  static String identity(Uri uri) {
    try {
      return DocumentsContract.buildDocumentUri(
              uri.getAuthority(), DocumentsContract.getDocumentId(uri))
          .toString();
    } catch (IllegalArgumentException error) {
      return uri.toString();
    }
  }
}
