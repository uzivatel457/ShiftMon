namespace ShiftMon.App;

internal static class ReleaseNotes
{
    public static string Current => Loc.T(
        """
        Verze 1.1.0
        • Instalační balíček funguje i na jiném počítači. .NET se nemusí instalovat zvlášť.
        • ShiftMon je v Nastavení Windows, v části Aplikace, a jde odtud odebrat.
        • Novější verze na GitHubu se nabídne sama a ukáže, co se změnilo.
        • Tlačítko v záhlaví je průhledné a sedí na lištu Windows 11.
        • Obrazovka v editoru se při tažení hýbe s kurzorem.
        • Vždy zůstane aspoň jeden profil a stejný displej se nezobrazí dvakrát.
        • Rozhraní je česky, anglicky, nebo podle jazyka Windows.
        """,
        """
        Version 1.1.0
        • The installer works on other PCs. .NET does not have to be installed separately.
        • ShiftMon is listed in Windows Settings, Apps, and can be removed there.
        • A newer GitHub release is offered automatically, with what changed.
        • The title-bar button is transparent and matches a Windows 11 caption.
        • A display follows the cursor while you drag it.
        • At least one profile always remains, and the same display is not listed twice.
        • The interface is Czech, English, or follows the Windows language.
        """);
}
