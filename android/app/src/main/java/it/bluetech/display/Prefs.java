package it.bluetech.display;

import android.content.Context;
import android.content.SharedPreferences;
import android.net.Uri;
import android.util.Base64;

import java.security.SecureRandom;
import java.security.spec.KeySpec;

import javax.crypto.SecretKeyFactory;
import javax.crypto.spec.PBEKeySpec;

/**
 * La configurazione dell'app: l'indirizzo dell'Agent e il codice di accesso (come il Player Windows: senza codice non si
 * cambia niente e non si esce). Il codice si salva come hash con il suo sale, mai in chiaro.
 */
final class Prefs {
    static final int PIN_MIN_LENGTH = 4;
    private static final int ITERATIONS = 20000;

    private final SharedPreferences store;

    Prefs(Context context) {
        store = context.getSharedPreferences("bluetech-display", Context.MODE_PRIVATE);
    }

    /** L'indirizzo dell'Agent, senza la pagina: http://192.168.1.20:5080 */
    String agent() {
        return store.getString("agent", "");
    }

    boolean configured() {
        return agent().length() > 0 && store.getString("pinHash", "").length() > 0;
    }

    void save(String agent, String newPin) {
        SharedPreferences.Editor editor = store.edit().putString("agent", agent);
        if (newPin != null) {
            byte[] salt = new byte[16];
            new SecureRandom().nextBytes(salt);
            editor.putString("pinSalt", Base64.encodeToString(salt, Base64.NO_WRAP))
                    .putString("pinHash", hash(newPin, salt));
        }
        editor.apply();
    }

    boolean checkPin(String pin) {
        String saltText = store.getString("pinSalt", "");
        String expected = store.getString("pinHash", "");
        if (saltText.isEmpty() || expected.isEmpty())
            return false;
        String actual = hash(pin, Base64.decode(saltText, Base64.NO_WRAP));
        // Confronto a tempo costante: la lunghezza è sempre la stessa.
        int diff = actual.length() ^ expected.length();
        for (int i = 0; i < Math.min(actual.length(), expected.length()); i++)
            diff |= actual.charAt(i) ^ expected.charAt(i);
        return diff == 0;
    }

    private static String hash(String pin, byte[] salt) {
        try {
            KeySpec spec = new PBEKeySpec(pin.toCharArray(), salt, ITERATIONS, 256);
            byte[] key = SecretKeyFactory.getInstance("PBKDF2WithHmacSHA1").generateSecret(spec).getEncoded();
            return Base64.encodeToString(key, Base64.NO_WRAP);
        } catch (Exception e) {
            throw new IllegalStateException("PBKDF2 non disponibile", e);
        }
    }

    /**
     * L'indirizzo scritto dal tecnico, ripulito: "192.168.1.20:5080", "http://pc-cassa:5080/auth" o ".../cliente"
     * diventano "http://192.168.1.20:5080" e "http://pc-cassa:5080". Null se non è un indirizzo.
     */
    static String normalize(String text) {
        if (text == null)
            return null;
        String value = text.trim();
        if (value.isEmpty())
            return null;
        if (!value.contains("://"))
            value = "http://" + value;
        Uri uri = Uri.parse(value);
        String scheme = uri.getScheme();
        String host = uri.getHost();
        if (host == null || host.isEmpty() || scheme == null
                || !(scheme.equalsIgnoreCase("http") || scheme.equalsIgnoreCase("https")))
            return null;
        StringBuilder result = new StringBuilder(scheme.toLowerCase()).append("://").append(host);
        if (uri.getPort() > 0)
            result.append(':').append(uri.getPort());
        return result.toString();
    }
}
