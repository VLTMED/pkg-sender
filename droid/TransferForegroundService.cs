using Android.App;
using Android.Content;
using Android.OS;

namespace PkgSender.Droid;

/// <summary>
/// Holds the transfer alive while the screen is locked or the app is in
/// the background. Without this, OEM battery managers (and Doze/App
/// Standby on stock Android) can suspend the socket mid-send a few
/// minutes after the activity loses foreground — looking on the PS4 side
/// exactly like a random "connection abort", because that is what it is.
/// The WakeLock/WifiLock move here (instead of living in the Activity)
/// so they survive activity recreation (rotation, low memory).
/// </summary>
// foregroundServiceType is declared on the <service> element in
// AndroidManifest.xml instead of here (safer across Mono.Android binding
// versions than guessing the enum member name for this SDK target).
[Service(Exported = false)]
public sealed class TransferForegroundService : Service
{
    const string ChannelId = "pkgsender.transfer";
    const int NotifId = 42;

    PowerManager.WakeLock? _cpu;
    global::Android.Net.Wifi.WifiManager.WifiLock? _wifi;

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        EnsureChannel();
        string title = intent?.GetStringExtra("title") ?? "PKG Sender";
        StartForeground(NotifId, BuildNotification(title, "جاري الإرسال إلى PS4…"));
        AcquireLocks();
        return StartCommandResult.Sticky;
    }

    /// <summary>Call from the UI thread to update the progress line shown in the notification.</summary>
    public static void UpdateProgress(Context ctx, string title, string status)
    {
        try
        {
            var mgr = (NotificationManager?)ctx.GetSystemService(NotificationService);
            var n = BuildNotificationStatic(ctx, title, status);
            mgr?.Notify(NotifId, n);
        }
        catch { }
    }

    void AcquireLocks()
    {
        try
        {
            var wifi = (global::Android.Net.Wifi.WifiManager?)GetSystemService(WifiService);
            _wifi = wifi?.CreateWifiLock(global::Android.Net.WifiMode.FullHighPerf, "pkgsender:fgsvc");
            _wifi?.SetReferenceCounted(false);
            _wifi?.Acquire();
        }
        catch { }
        try
        {
            var pm = (PowerManager?)GetSystemService(PowerService);
            // No fixed timeout here: a foreground service is already exempt
            // from Doze, so the lock just needs to outlive the service —
            // released explicitly in OnDestroy, not by an expiring timer
            // (the old 30-minute Activity-side timer could lapse mid-send
            // on large PKGs; this one can't).
            _cpu = pm?.NewWakeLock(WakeLockFlags.Partial, "pkgsender:fgsvc");
            _cpu?.SetReferenceCounted(false);
            _cpu?.Acquire();
        }
        catch { }
    }

    public override void OnDestroy()
    {
        try { if (_wifi?.IsHeld == true) _wifi.Release(); } catch { }
        try { if (_cpu?.IsHeld == true) _cpu.Release(); } catch { }
        try { _wifi?.Dispose(); } catch { }
        try { _cpu?.Dispose(); } catch { }
        base.OnDestroy();
    }

    void EnsureChannel()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O) return;
        var mgr = (NotificationManager?)GetSystemService(NotificationService);
        if (mgr?.GetNotificationChannel(ChannelId) != null) return;
        var ch = new NotificationChannel(ChannelId, "إرسال PKG", NotificationImportance.Low)
        {
            Description = "إشعار أثناء إرسال ملفات PKG إلى PS4/PS5",
        };
        mgr?.CreateNotificationChannel(ch);
    }

    Notification BuildNotification(string title, string status) => BuildNotificationStatic(this, title, status);

    static Notification BuildNotificationStatic(Context ctx, string title, string status)
    {
        // Plain framework Notification.Builder (Mono.Android, no extra NuGet
        // package): the (context, channelId) ctor needs API 26+, which
        // matches this project's SupportedOSPlatformVersion (26).
        var builder = new Notification.Builder(ctx, ChannelId)
            .SetContentTitle(title)
            .SetContentText(status)
            .SetSmallIcon(Resource.Drawable.logo)
            .SetOngoing(true)
            .SetOnlyAlertOnce(true);
        return builder.Build();
    }

    public static void Start(Context ctx, string title)
    {
        var i = new Intent(ctx, typeof(TransferForegroundService));
        i.PutExtra("title", title);
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            ctx.StartForegroundService(i);
        else
            ctx.StartService(i);
    }

    public static void Stop(Context ctx) => ctx.StopService(new Intent(ctx, typeof(TransferForegroundService)));
}
