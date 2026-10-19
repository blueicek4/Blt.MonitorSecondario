using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Xml.Serialization;

namespace Bluetech.Display.Player;

public enum MissingScreenBehavior
{
    /// <summary>Il player si nasconde e ricompare quando lo schermo torna: non copre mai la cassa.</summary>
    Hide,
    /// <summary>Il player si apre in finestra sullo schermo principale (utile per prove e installazioni a un monitor).</summary>
    WindowedOnPrimary,
}

[XmlRoot("BluetechDisplayPlayer")]
public sealed class PlayerSettings
{
    public string Url { get; set; } = "";

    /// <summary>DeviceName dello schermo (es. \\.\DISPLAY2). Vuoto = primo schermo secondario.</summary>
    public string Screen { get; set; } = "";

    public MissingScreenBehavior WhenScreenMissing { get; set; } = MissingScreenBehavior.Hide;
    public bool FullScreen { get; set; } = true;
    public bool TopMost { get; set; } = true;
    public bool HideCursor { get; set; } = true;
    public bool PreventSleep { get; set; } = true;
    public int ZoomPercent { get; set; } = 100;

    /// <summary>Ricarica periodica della pagina in minuti, 0 = mai.</summary>
    public int ReloadMinutes { get; set; }

    public string PinHash { get; set; } = "";
    public string PinSalt { get; set; } = "";
    public int PinIterations { get; set; }

    [XmlIgnore]
    public bool IsConfigured => !string.IsNullOrEmpty(PinHash) && TryNormalizeUrl(Url, out _);

    public static string Folder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Bluetech", "DisplayPlayer");

    public static string FilePath => Path.Combine(Folder, "player.config.xml");

    public PlayerSettings Clone() => (PlayerSettings)MemberwiseClone();

    public static PlayerSettings Load()
    {
        if (!File.Exists(FilePath))
            return new PlayerSettings();

        try
        {
            using var stream = File.OpenRead(FilePath);
            var settings = (PlayerSettings)new XmlSerializer(typeof(PlayerSettings)).Deserialize(stream);
            settings.ZoomPercent = Clamp(settings.ZoomPercent, 25, 300);
            settings.ReloadMinutes = Clamp(settings.ReloadMinutes, 0, 1440);
            return settings;
        }
        catch (Exception ex)
        {
            // File illeggibile: lo mettiamo da parte e ripartiamo dalla prima configurazione.
            Log.Error("Configurazione illeggibile, viene ignorata", ex);
            try { File.Move(FilePath, FilePath + "." + DateTime.Now.ToString("yyyyMMddHHmmss") + ".bad"); } catch { }
            return new PlayerSettings();
        }
    }

    public void Save()
    {
        EnsureFolder();
        var temp = FilePath + ".tmp";
        using (var stream = File.Create(temp))
            new XmlSerializer(typeof(PlayerSettings)).Serialize(stream, this);

        if (File.Exists(FilePath))
            File.Replace(temp, FilePath, null);
        else
            File.Move(temp, FilePath);
    }

    /// <summary>
    /// Crea la cartella in ProgramData dando agli utenti il permesso di modifica,
    /// così la configurazione fatta dal tecnico (anche come amministratore) resta modificabile dall'utente della cassa.
    /// </summary>
    public static void EnsureFolder()
    {
        if (Directory.Exists(Folder))
            return;

        var security = new DirectorySecurity();
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.Modify,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));

        try { Directory.CreateDirectory(Folder, security); }
        catch (UnauthorizedAccessException) { Directory.CreateDirectory(Folder); }
    }

    /// <summary>Accetta "192.168.1.10:5080/display" aggiungendo http://. Ammessi http, https e file.</summary>
    public static bool TryNormalizeUrl(string text, out string url)
    {
        url = (text ?? "").Trim();
        if (url.Length == 0)
            return false;
        if (!url.Contains("://"))
            url = "http://" + url;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        url = uri.AbsoluteUri;
        return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeFile;
    }

    static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(max, value));
}
