using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MonAlbumPc.App.Albums;
using MonAlbumPc.Core.Model;
using MonAlbumPc.Core.Pairing;
using MonAlbumPc.Server;

namespace MonAlbumPc.App.Phones;

/// <summary>"Ajouter un téléphone": choose the album, show the QR code and the 6-digit code, wait for the phone.</summary>
public partial class AddPhoneDialog : Window
{
    private readonly AppHost _host;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private PairingSession? _session;
    private Action? _primaryAction;

    public AddPhoneDialog(AppHost host, Guid? albumId)
    {
        _host = host;
        InitializeComponent();
        _timer.Tick += (_, _) => UpdateCountdown();
        _host.Core.Pairing.Paired += OnPaired;
        Closed += (_, _) => Cleanup();
        LoadAlbums(albumId);
        ShowAlbumStep();
    }

    /// <summary>Album of the phone paired by this wizard, if any.</summary>
    public Guid? PairedAlbumId { get; private set; }

    private void LoadAlbums(Guid? selected)
    {
        var choices = _host.Core.Albums.List().Select(a =>
        {
            var phones = _host.Core.Devices.ListByAlbum(a.Id).Count;
            var detail = $"{a.DestinationRoot} · {Formatting.Plural(phones, "téléphone", "téléphones")}";
            return new AlbumChoice(a, a.Name, detail, (Brush)new BrushConverter().ConvertFromString(a.Color)!,
                Brushes.White, a.Name[..1].ToUpperInvariant(), SystemFonts.MessageFontFamily);
        }).ToList();
        choices.Add(new AlbumChoice(null, "Créer un nouvel album", "Pour une nouvelle personne",
            (Brush)FindResource("SubtleFillColorSecondaryBrush"), (Brush)FindResource("TextFillColorPrimaryBrush"),
            "", (FontFamily)FindResource("IconFont")));
        AlbumChoices.ItemsSource = choices;
        AlbumChoices.SelectedItem = choices.FirstOrDefault(c => c.Album?.Id == selected) ?? choices[0];
    }

    private AlbumChoice? SelectedChoice => AlbumChoices.SelectedItem as AlbumChoice;

    private void ShowAlbumStep()
    {
        Show(AlbumStep, "Étape 1 sur 3");
        PrimaryButton.Content = "Suivant";
        _primaryAction = OnAlbumChosen;
        UpdateAlbumStep();
    }

    private void OnAlbumChoiceChanged(object sender, SelectionChangedEventArgs e) => UpdateAlbumStep();

    private void OnNewAlbumNameChanged(object sender, TextChangedEventArgs e) => UpdateAlbumStep();

    private void UpdateAlbumStep()
    {
        var isNew = SelectedChoice is { Album: null };
        NewAlbumPanel.Visibility = isNew ? Visibility.Visible : Visibility.Collapsed;
        NewAlbumFolder.Text = $"Dossier : {AlbumDialog.SuggestFolder(_host.Core, NewAlbumName.Text)}";
        PrimaryButton.IsEnabled = SelectedChoice is not null && (!isNew || NewAlbumName.Text.Trim().Length > 0);
        if (isNew && IsLoaded)
        {
            NewAlbumName.Focus();
        }
    }

    private void OnAlbumChosen()
    {
        var album = SelectedChoice?.Album;
        if (album is null)
        {
            var name = NewAlbumName.Text.Trim();
            var folder = AlbumDialog.SuggestFolder(_host.Core, name);
            try
            {
                Directory.CreateDirectory(folder);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(this, $"Impossible de créer le dossier {folder}.\n\n{e.Message}", Title,
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            album = new Album(Guid.NewGuid(), name, folder, _host.Core.Albums.NextColor(), _host.Core.Clock.GetLocalNow());
            _host.Core.Albums.Add(album);
            LoadAlbums(album.Id);
        }
        StartPairing(album);
    }

    private void StartPairing(Album album)
    {
        if (_host.ServerError is { } error)
        {
            ShowMessage("", "SystemFillColorCriticalBrush", "Le téléphone ne pourra pas joindre ce PC", error,
                "Fermer", Close);
            return;
        }
        if (_host.LanAddress is not { } address)
        {
            ShowMessage("", "SystemFillColorCriticalBrush", "Ce PC n'est connecté à aucun réseau",
                "Connectez le PC au Wi-Fi ou au câble de la box, puis réessayez.", "Réessayer", () => StartPairing(album));
            return;
        }

        _session = _host.Core.Pairing.Start(album.Id);
        var link = PairingLink.Build(address, _host.DownloadPort, _host.ApiPort, _session,
            _host.Core.CertificateFingerprint, _host.Core.PcName);
        QrImage.Source = QrCodes.Render(link);
        CodeRun.Text = $"{_session.Code[..3]} {_session.Code[3..]}";

        Show(QrStep, "Étape 2 sur 3");
        PrimaryButton.Content = "En attente du téléphone…";
        PrimaryButton.IsEnabled = false;
        _primaryAction = null;
        UpdateCountdown();
        _timer.Start();
    }

    private void UpdateCountdown()
    {
        if (_session is null)
        {
            return;
        }
        var remaining = _session.ExpiresAt - _host.Core.Clock.GetUtcNow();
        if (remaining <= TimeSpan.Zero || _host.Core.Pairing.Current is null)
        {
            _timer.Stop();
            var album = _session.AlbumId;
            _session = null;
            ShowMessage("", "SystemFillColorCautionBrush", "Ce code a expiré",
                "Pour votre sécurité, un code n'est valable que 5 minutes. Générez-en un nouveau et scannez-le.",
                "Générer un nouveau code", () => StartPairing(_host.Core.Albums.Get(album)!));
            return;
        }
        Countdown.Text = $"Code valable encore {(int)remaining.TotalMinutes}:{remaining.Seconds:00}";
    }

    private void OnRegenerate(object sender, RoutedEventArgs e)
    {
        if (_session is { } session && _host.Core.Albums.Get(session.AlbumId) is { } album)
        {
            StartPairing(album);
        }
    }

    private void OnPaired(PairingResult result) => Dispatcher.BeginInvoke(() =>
    {
        if (_session is null || result.Album.Id != _session.AlbumId)
        {
            return;
        }
        _timer.Stop();
        _session = null;
        PairedAlbumId = result.Album.Id;
        CancelButton.Visibility = Visibility.Collapsed;
        ShowMessage("", "SystemFillColorSuccessBrush", $"{result.Device.Name} est relié à l'album {result.Album.Name}.",
            "La première sauvegarde va commencer. Vous pouvez fermer cette fenêtre.", "Terminer", () => DialogResult = true);
        StepText.Text = "Étape 3 sur 3";
    });

    private void ShowMessage(string icon, string brush, string title, string text, string action, Action onAction)
    {
        Show(MessageStep, StepText.Text);
        MessageIcon.Text = icon;
        MessageIcon.SetResourceReference(ForegroundProperty, brush);
        MessageTitle.Text = title;
        MessageText.Text = text;
        PrimaryButton.Content = action;
        PrimaryButton.IsEnabled = true;
        _primaryAction = onAction;
    }

    private void Show(FrameworkElement step, string stepText)
    {
        foreach (var panel in new FrameworkElement[] { AlbumStep, QrStep, MessageStep })
        {
            panel.Visibility = panel == step ? Visibility.Visible : Visibility.Collapsed;
        }
        StepText.Text = stepText;
    }

    private void OnPrimary(object sender, RoutedEventArgs e) => _primaryAction?.Invoke();

    private void Cleanup()
    {
        _timer.Stop();
        _host.Core.Pairing.Paired -= OnPaired;
        if (_session is not null)
        {
            _host.Core.Pairing.Cancel();
        }
    }

    private sealed record AlbumChoice(Album? Album, string Name, string Detail, Brush Color, Brush Foreground, string Initial, FontFamily Font);
}
