using System;
using System.Drawing;
using System.Windows.Forms;

namespace Bluetech.Display.Player;

/// <summary>
/// Lo schermo di vetro del monitor touch accanto alla cassa (docs/03 §7.2). Una finestra trasparente sopra il browser,
/// con WS_EX_NOACTIVATE come la tastiera a video di Windows: riceve il tocco senza mai diventare la finestra attiva. Il
/// tocco non arriva al browser, che quindi non può chiedere il focus per conto suo: il Player lo passa alla pagina come
/// clic sul punto toccato (window.btTap). Solo tocchi semplici: niente scorrimento né trascinamento, che sul monitor del
/// cliente non servono (l'unico comando è il pulsante della copia di carta).
/// </summary>
sealed class TouchShield : Form
{
    readonly Action<Point> _tap;

    /// <param name="tap">Il punto toccato, in coordinate dello schermo.</param>
    public TouchShield(Action<Point> tap)
    {
        _tap = tap;
        Text = "Bluetech Display Player — tocchi";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Black;
        // Quasi trasparente, non del tutto: dove una finestra sovrapposta è trasparente al 100% Windows lascia passare
        // il tocco alla finestra sotto, cioè al browser. Al 1% il nero non si vede.
        Opacity = 0.01;
    }

    /// <summary>Come il Player: con "Nascondi il puntatore del mouse" niente freccia nemmeno sul vetro.</summary>
    public bool HideCursor { get; set; }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case NativeMethods.WM_MOUSEACTIVATE:
                m.Result = (IntPtr)NativeMethods.MA_NOACTIVATE;
                return;
            case NativeMethods.WM_SETCURSOR when HideCursor:
                NativeMethods.SetCursor(IntPtr.Zero);
                m.Result = (IntPtr)1;
                return;
            case NativeMethods.WM_LBUTTONUP:
                // Il tocco arriva come clic del mouse (Windows lo traduce): si passa alla pagina quando il dito si alza.
                var value = m.LParam.ToInt64();
                var point = new Point((short)(value & 0xFFFF), (short)((value >> 16) & 0xFFFF));
                try
                {
                    _tap(PointToScreen(point));
                }
                catch (Exception ex)
                {
                    Log.Error("Tocco sullo schermo di vetro", ex);
                }
                break;
        }
        base.WndProc(ref m);
    }
}
