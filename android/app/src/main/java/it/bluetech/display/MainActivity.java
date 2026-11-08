package it.bluetech.display;

import android.annotation.SuppressLint;
import android.app.Activity;
import android.app.AlertDialog;
import android.content.Intent;
import android.graphics.Color;
import android.net.Uri;
import android.net.http.SslError;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.os.SystemClock;
import android.provider.Settings;
import android.text.InputType;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.MotionEvent;
import android.view.View;
import android.view.ViewGroup;
import android.view.WindowInsets;
import android.view.WindowInsetsController;
import android.view.WindowManager;
import android.webkit.CookieManager;
import android.webkit.RenderProcessGoneDetail;
import android.webkit.SslErrorHandler;
import android.webkit.WebChromeClient;
import android.webkit.WebResourceError;
import android.webkit.WebResourceRequest;
import android.webkit.WebResourceResponse;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.Button;
import android.widget.EditText;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.TextView;
import android.widget.Toast;

/**
 * Il monitor cliente su un tablet Android: la pagina dell'Agent a schermo intero, come il Player su Windows.
 * <ul>
 * <li>si apre su /auth: la prima volta mostra il codice da scrivere nel back office (Schermi), poi va da sola su
 * /cliente; il tablet resta abbinato (i cookie restano);</li>
 * <li>schermo sempre acceso, senza barre di sistema, il tasto Indietro non esce;</li>
 * <li>se l'Agent non risponde, "Mi ricollego alla cassa…" e riprova da sola (5 s, poi fino a 60 s);</li>
 * <li>configurazione e uscita protette da codice: cinque tocchi di fila nell'angolo in alto a sinistra;</li>
 * <li>si può scegliere come app della schermata Home: il tablet si accende sul monitor e Home ci torna.</li>
 * </ul>
 */
public final class MainActivity extends Activity {
    private static final int CORNER_DP = 96;
    private static final int CORNER_TAPS = 5;
    private static final long CORNER_WINDOW_MS = 3000;
    private static final int MAX_WRONG_PINS = 5;
    private static final long LOCK_MS = 60_000;

    private final Handler handler = new Handler(Looper.getMainLooper());
    private final Runnable retry = this::load;

    private Prefs prefs;
    private FrameLayout root;
    private WebView web;
    private TextView status;
    private int retrySeconds = 5;
    private boolean mainFrameFailed;
    private int cornerTaps;
    private long firstCornerTap;
    private int wrongPins;
    private long lockedUntil;
    private AlertDialog dialog;

    @Override
    protected void onCreate(Bundle state) {
        super.onCreate(state);
        prefs = new Prefs(this);
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);

        root = new FrameLayout(this);
        root.setBackgroundColor(Color.rgb(11, 16, 23));
        status = new TextView(this);
        status.setTextColor(Color.WHITE);
        status.setTextSize(TypedValue.COMPLEX_UNIT_SP, 22);
        status.setGravity(Gravity.CENTER);
        status.setPadding(dp(32), dp(32), dp(32), dp(32));
        status.setBackgroundColor(Color.rgb(14, 17, 23));
        createWebView();
        root.addView(status, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
        setContentView(root);

        if (prefs.configured()) {
            load();
        } else {
            showStatus("Bluetech Display\n\nPrima configurazione");
            handler.post(() -> showSettings(true));
        }
    }

    @SuppressLint("SetJavaScriptEnabled")
    private void createWebView() {
        web = new WebView(this);
        web.setBackgroundColor(Color.rgb(11, 16, 23));
        WebSettings settings = web.getSettings();
        settings.setJavaScriptEnabled(true);
        settings.setDomStorageEnabled(true);
        settings.setMediaPlaybackRequiresUserGesture(false);   // i video delle vetrine partono da soli
        settings.setTextZoom(100);                              // la dimensione del testo di Android non sposta il layout
        settings.setSupportZoom(false);
        settings.setBuiltInZoomControls(false);
        settings.setDisplayZoomControls(false);
        // La pagina riconosce l'app e non propone di installarla come PWA.
        settings.setUserAgentString(settings.getUserAgentString() + " BluetechDisplayAndroid/" + BuildConfig.VERSION_NAME);
        CookieManager.getInstance().setAcceptCookie(true);
        CookieManager.getInstance().setAcceptThirdPartyCookies(web, false);

        web.setWebChromeClient(new WebChromeClient());
        web.setWebViewClient(new WebViewClient() {
            @Override
            public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest request) {
                // Solo le pagine dell'Agent: un link verso un altro sito non trasforma il monitor in un browser.
                return !sameAgent(request.getUrl());
            }

            @Override
            public void onPageStarted(WebView view, String url, android.graphics.Bitmap favicon) {
                mainFrameFailed = false;
            }

            @Override
            public void onPageFinished(WebView view, String url) {
                if (mainFrameFailed)
                    return;
                status.setVisibility(View.GONE);
                retrySeconds = 5;
                CookieManager.getInstance().flush();   // l'abbinamento resta anche se il tablet si spegne di colpo
            }

            @Override
            public void onReceivedError(WebView view, WebResourceRequest request, WebResourceError error) {
                if (request.isForMainFrame())
                    failed(String.valueOf(error.getDescription()));
            }

            @Override
            public void onReceivedHttpError(WebView view, WebResourceRequest request, WebResourceResponse response) {
                if (request.isForMainFrame() && response.getStatusCode() >= 500)
                    failed("HTTP " + response.getStatusCode());
            }

            @Override
            public void onReceivedSslError(WebView view, SslErrorHandler sslHandler, SslError error) {
                // Mai accettare un certificato non valido in silenzio: nella rete del locale si usa http://, oppure si
                // installa sul tablet il certificato dell'Agent.
                sslHandler.cancel();
                failed("certificato HTTPS non valido per questo tablet: usa l'indirizzo http:// dell'Agent o installa il suo certificato");
            }

            @Override
            public boolean onRenderProcessGone(WebView view, RenderProcessGoneDetail detail) {
                // Il motore della pagina si è chiuso: se ne crea uno nuovo invece di chiudere l'app.
                root.removeView(web);
                web.destroy();
                createWebView();
                load();
                return true;
            }
        });
        root.addView(web, 0, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
    }

    private void load() {
        handler.removeCallbacks(retry);
        String agent = prefs.agent();
        if (agent.isEmpty())
            return;
        mainFrameFailed = false;
        if (status.getVisibility() != View.VISIBLE)
            showStatus("Collegamento a " + agent + "…");
        web.loadUrl(agent + "/auth");
    }

    private void failed(String description) {
        mainFrameFailed = true;
        showStatus("Mi ricollego alla cassa…\n\n" + prefs.agent() + "\n" + description + "\n\nNuovo tentativo tra " + retrySeconds + " s");
        handler.removeCallbacks(retry);
        handler.postDelayed(retry, retrySeconds * 1000L);
        retrySeconds = Math.min(60, retrySeconds * 2);
    }

    private void showStatus(String text) {
        status.setText(text);
        status.setVisibility(View.VISIBLE);
    }

    private boolean sameAgent(Uri url) {
        Uri agent = Uri.parse(prefs.agent());
        return agent.getHost() != null
                && agent.getHost().equalsIgnoreCase(url.getHost())
                && String.valueOf(agent.getScheme()).equalsIgnoreCase(url.getScheme())
                && port(agent) == port(url);
    }

    private static int port(Uri uri) {
        if (uri.getPort() > 0)
            return uri.getPort();
        return "https".equalsIgnoreCase(uri.getScheme()) ? 443 : 80;
    }

    /** Cinque tocchi di fila nell'angolo in alto a sinistra aprono la configurazione; la pagina riceve i tocchi lo stesso. */
    @Override
    public boolean dispatchTouchEvent(MotionEvent event) {
        if (event.getActionMasked() == MotionEvent.ACTION_DOWN && event.getX() < dp(CORNER_DP) && event.getY() < dp(CORNER_DP)) {
            long now = SystemClock.elapsedRealtime();
            if (cornerTaps == 0 || now - firstCornerTap > CORNER_WINDOW_MS) {
                cornerTaps = 0;
                firstCornerTap = now;
            }
            if (++cornerTaps >= CORNER_TAPS) {
                cornerTaps = 0;
                askPin();
            }
        }
        return super.dispatchTouchEvent(event);
    }

    @Override
    @SuppressWarnings("deprecation")
    public void onBackPressed() {
        // Monitor del cliente: Indietro non esce e non torna alla pagina prima.
    }

    private void askPin() {
        if (dialog != null && dialog.isShowing())
            return;
        long now = SystemClock.elapsedRealtime();
        if (now < lockedUntil) {
            Toast.makeText(this, "Troppi codici errati: riprova tra " + ((lockedUntil - now) / 1000 + 1) + " s", Toast.LENGTH_LONG).show();
            return;
        }
        EditText pin = new EditText(this);
        pin.setInputType(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_VARIATION_PASSWORD);
        pin.setHint("Codice di accesso");
        dialog = new AlertDialog.Builder(this)
                .setTitle("Bluetech Display")
                .setView(padded(pin))
                .setPositiveButton("Conferma", (d, w) -> {
                    if (prefs.checkPin(pin.getText().toString())) {
                        wrongPins = 0;
                        showMenu();
                    } else if (++wrongPins >= MAX_WRONG_PINS) {
                        wrongPins = 0;
                        lockedUntil = SystemClock.elapsedRealtime() + LOCK_MS;
                        Toast.makeText(this, "Troppi codici errati: bloccato per 60 s", Toast.LENGTH_LONG).show();
                    } else {
                        Toast.makeText(this, "Codice errato", Toast.LENGTH_SHORT).show();
                    }
                    hideSystemBars();
                })
                .setNegativeButton("Annulla", (d, w) -> hideSystemBars())
                .create();
        dialog.show();
    }

    private void showMenu() {
        String[] items = {
                "Indirizzo dell'Agent e codice",
                "Ricarica la pagina",
                "Scegli l'app della schermata Home",
                "Impostazioni di Android",
                "Esci dall'app",
        };
        dialog = new AlertDialog.Builder(this)
                .setTitle("Bluetech Display " + BuildConfig.VERSION_NAME)
                .setItems(items, (d, which) -> {
                    switch (which) {
                        case 0:
                            showSettings(false);
                            break;
                        case 1:
                            retrySeconds = 5;
                            load();
                            break;
                        case 2:
                            open(Settings.ACTION_HOME_SETTINGS);
                            break;
                        case 3:
                            open(Settings.ACTION_SETTINGS);
                            break;
                        default:
                            finishAndRemoveTask();
                            break;
                    }
                })
                .setNegativeButton("Chiudi", (d, w) -> hideSystemBars())
                .create();
        dialog.show();
    }

    /** Una pagina delle impostazioni di Android; alcuni tablet non hanno quella della schermata Home. */
    private void open(String action) {
        try {
            startActivity(new Intent(action));
        } catch (Exception e) {
            try {
                startActivity(new Intent(Settings.ACTION_SETTINGS));
            } catch (Exception ignored) {
                Toast.makeText(this, "Impostazioni di Android non disponibili", Toast.LENGTH_LONG).show();
            }
        }
    }

    private void showSettings(boolean firstRun) {
        LinearLayout form = new LinearLayout(this);
        form.setOrientation(LinearLayout.VERTICAL);
        TextView help = new TextView(this);
        help.setText("Indirizzo dell'Agent nella rete del locale, per esempio 192.168.1.20:5080. Poi lo schermo mostra un codice: "
                + "scrivilo nel back office, in Schermi.");
        EditText agent = new EditText(this);
        agent.setInputType(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_VARIATION_URI);
        agent.setHint("192.168.1.20:5080");
        agent.setText(prefs.agent());
        EditText pin = new EditText(this);
        pin.setInputType(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_VARIATION_PASSWORD);
        pin.setHint(firstRun ? "Codice di accesso (almeno 4 caratteri)" : "Nuovo codice (vuoto = non cambia)");
        EditText confirm = new EditText(this);
        confirm.setInputType(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_VARIATION_PASSWORD);
        confirm.setHint("Conferma il codice");
        form.addView(help);
        form.addView(agent);
        form.addView(pin);
        form.addView(confirm);

        AlertDialog.Builder builder = new AlertDialog.Builder(this)
                .setTitle(firstRun ? "Bluetech Display — prima configurazione" : "Bluetech Display — configurazione")
                .setView(padded(form))
                .setCancelable(!firstRun)
                .setPositiveButton("Salva", null);
        if (!firstRun)
            builder.setNegativeButton("Annulla", (d, w) -> hideSystemBars());
        dialog = builder.create();
        dialog.setOnShowListener(d -> {
            Button save = dialog.getButton(AlertDialog.BUTTON_POSITIVE);
            save.setOnClickListener(v -> {
                String address = Prefs.normalize(agent.getText().toString());
                if (address == null) {
                    agent.setError("Indirizzo non valido. Esempio: 192.168.1.20:5080");
                    return;
                }
                String newPin = pin.getText().toString();
                boolean changePin = firstRun || newPin.length() > 0 || confirm.getText().length() > 0;
                if (changePin && newPin.length() < Prefs.PIN_MIN_LENGTH) {
                    pin.setError("Almeno " + Prefs.PIN_MIN_LENGTH + " caratteri");
                    return;
                }
                if (changePin && !newPin.equals(confirm.getText().toString())) {
                    confirm.setError("I due codici non coincidono");
                    return;
                }
                prefs.save(address, changePin ? newPin : null);
                dialog.dismiss();
                hideSystemBars();
                retrySeconds = 5;
                load();
            });
        });
        dialog.show();
    }

    private View padded(View view) {
        FrameLayout frame = new FrameLayout(this);
        frame.setPadding(dp(24), dp(12), dp(24), dp(4));
        frame.addView(view);
        return frame;
    }

    private int dp(int value) {
        return Math.round(value * getResources().getDisplayMetrics().density);
    }

    @Override
    public void onWindowFocusChanged(boolean hasFocus) {
        super.onWindowFocusChanged(hasFocus);
        if (hasFocus)
            hideSystemBars();
    }

    /** Schermo intero: le barre di sistema compaiono solo con uno scorrimento dal bordo, e poi spariscono da sole. */
    @SuppressWarnings("deprecation")
    private void hideSystemBars() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            getWindow().setDecorFitsSystemWindows(false);
            WindowInsetsController controller = getWindow().getInsetsController();
            if (controller != null) {
                controller.hide(WindowInsets.Type.systemBars());
                controller.setSystemBarsBehavior(WindowInsetsController.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE);
            }
        } else {
            getWindow().getDecorView().setSystemUiVisibility(View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY
                    | View.SYSTEM_UI_FLAG_FULLSCREEN
                    | View.SYSTEM_UI_FLAG_HIDE_NAVIGATION
                    | View.SYSTEM_UI_FLAG_LAYOUT_STABLE
                    | View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION
                    | View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN);
        }
    }

    @Override
    protected void onResume() {
        super.onResume();
        web.onResume();
        hideSystemBars();
    }

    @Override
    protected void onPause() {
        web.onPause();
        CookieManager.getInstance().flush();
        super.onPause();
    }

    @Override
    protected void onDestroy() {
        handler.removeCallbacksAndMessages(null);
        if (dialog != null)
            dialog.dismiss();
        web.destroy();
        super.onDestroy();
    }
}
