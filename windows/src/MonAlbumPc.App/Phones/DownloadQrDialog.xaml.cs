using System.Windows;

namespace MonAlbumPc.App.Phones;

/// <summary>QR code of the download page, to install the Android app without pairing yet.</summary>
public partial class DownloadQrDialog : Window
{
    public DownloadQrDialog(string url, string versionName)
    {
        InitializeComponent();
        QrImage.Source = QrCodes.Render(url);
        Explanation.Text = "Scannez ce code avec l'appareil photo du téléphone Android pour télécharger " +
                           $"Mon Album PC (version {versionName}).";
        Link.Text = url;
    }
}
