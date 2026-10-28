using System.Windows.Controls;
using VideoCapture.Services;
using CaptureMode = VideoCapture.Services.CaptureMode;

namespace VideoCapture;

public partial class MainWindow
{
    private IReadOnlyList<CaptureMode> _captureModes = Array.Empty<CaptureMode>();
    private bool _updatingCaptureOptions;

    private void ClearCaptureOptions()
    {
        _updatingCaptureOptions = true;
        try
        {
            _captureModes = Array.Empty<CaptureMode>();
            CaptureResolutions.ItemsSource = CaptureFrameRates.ItemsSource = CaptureFormats.ItemsSource = null;
            SetCaptureControlsEnabled(false);
        }
        finally { _updatingCaptureOptions = false; }
    }

    private void SetCaptureModes(IReadOnlyList<CaptureMode> modes)
    {
        _captureModes = modes;
        CaptureMode? preferred = null;
        if (_selectedVideoName is not null) _preferences.CaptureModes.TryGetValue(_selectedVideoName, out preferred);
        _updatingCaptureOptions = true;
        try
        {
            CaptureResolutions.ItemsSource = modes.Select(m => new CaptureResolution(m.Width, m.Height)).Distinct()
                .OrderByDescending(r => (long)r.Width * r.Height).ToArray();
            CaptureResolutions.SelectedItem = CaptureResolutions.Items.Cast<CaptureResolution>()
                .FirstOrDefault(r => r.Width == preferred?.Width && r.Height == preferred.Height)
                ?? CaptureResolutions.Items.Cast<CaptureResolution>().FirstOrDefault();
        }
        finally { _updatingCaptureOptions = false; }
        PopulateFrameRates(preferred?.FramesPerSecond, preferred?.Chroma);
        if (preferred is null && _selectedVideoName is not null && GetSelectedCaptureMode() is { } initialMode)
            _preferences.CaptureModes[_selectedVideoName] = initialMode;
        QueuePreferencesSave();
    }

    private void CaptureResolution_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingCaptureOptions) return;
        PopulateFrameRates();
        CaptureSetting_Changed(sender, e);
    }

    private void PopulateFrameRates(double? preferredFps = null, string? preferredChroma = null)
    {
        var previous = CaptureFrameRates.SelectedItem as CaptureFrameRate;
        var resolution = CaptureResolutions.SelectedItem as CaptureResolution;
        var rates = _captureModes.Where(m => resolution is not null && m.Width == resolution.Width && m.Height == resolution.Height)
            .Select(m => new CaptureFrameRate(Math.Round(m.FramesPerSecond, 2))).Distinct()
            .OrderByDescending(r => r.FramesPerSecond).ToArray();
        _updatingCaptureOptions = true;
        try
        {
            CaptureFrameRates.ItemsSource = rates;
            CaptureFrameRates.SelectedItem = rates.FirstOrDefault(r => preferredFps is { } fps && Math.Abs(r.FramesPerSecond - fps) < 0.011)
                ?? rates.FirstOrDefault(r => r == previous) ?? rates.FirstOrDefault();
        }
        finally { _updatingCaptureOptions = false; }
        PopulateFormats(preferredChroma);
    }

    private void CaptureFrameRate_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingCaptureOptions) return;
        PopulateFormats();
        CaptureSetting_Changed(sender, e);
    }

    private void PopulateFormats(string? preferred = null)
    {
        string? previous = preferred ?? CaptureFormats.SelectedItem as string;
        var formats = MatchingSizeAndRate().Select(m => m.Chroma).Distinct().Order().ToArray();
        bool updating = _updatingCaptureOptions;
        _updatingCaptureOptions = true;
        try
        {
            CaptureFormats.ItemsSource = formats;
            CaptureFormats.SelectedItem = formats.FirstOrDefault(f => f == previous)
                ?? formats.FirstOrDefault(f => f == "MJPG") ?? formats.FirstOrDefault();
        }
        finally { _updatingCaptureOptions = updating; }
        if (IsLoaded) UpdateControls();
    }

    private IEnumerable<CaptureMode> MatchingSizeAndRate()
    {
        if (CaptureResolutions.SelectedItem is not CaptureResolution resolution || CaptureFrameRates.SelectedItem is not CaptureFrameRate rate)
            return Array.Empty<CaptureMode>();
        return _captureModes.Where(m => m.Width == resolution.Width && m.Height == resolution.Height &&
            Math.Abs(m.FramesPerSecond - rate.FramesPerSecond) < 0.011);
    }

    private CaptureMode? GetSelectedCaptureMode() => MatchingSizeAndRate().FirstOrDefault(m => m.Chroma == CaptureFormats.SelectedItem as string);

    private void SetCaptureControlsEnabled(bool enabled)
    {
        CaptureResolutions.IsEnabled = enabled && CaptureResolutions.Items.Count > 0;
        CaptureFrameRates.IsEnabled = enabled && CaptureFrameRates.Items.Count > 0;
        CaptureFormats.IsEnabled = enabled && CaptureFormats.Items.Count > 0;
    }
}
