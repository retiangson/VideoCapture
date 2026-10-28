using System.Windows;

namespace VideoCapture;

public partial class MainWindow
{
    private CecRemoteWindow? _cecRemote;
    private void Cec_Click(object sender, RoutedEventArgs e)
    {
        if (_closing) return;
        if (_cecRemote is null)
        {
            _cecRemote = new CecRemoteWindow { Owner = this };
            _cecRemote.Closed += (_, _) => _cecRemote = null;
        }
        _cecRemote.Show();
        _cecRemote.Activate();
    }
}
