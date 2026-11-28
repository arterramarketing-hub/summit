using System.Runtime.CompilerServices;

// The test tools (Assets/Editor) read the game's parts directly: SummitGame's rider, body, terrain and the rest are
// internal, not private, so a test says game.rider rather than looking it up by name.
[assembly: InternalsVisibleTo("Assembly-CSharp-Editor")]
