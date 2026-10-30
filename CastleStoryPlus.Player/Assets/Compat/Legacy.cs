// Engine types Unity 5.6 had and Unity 6 no longer has (or keeps only as empty stubs), with the members the game uses.
// CodePatcher points the game's references to them here.
// Unity 6 keeps empty, unusable GUIElement/GUIText/GUITexture stubs; these replace them for the game.
#pragma warning disable CS0436
namespace UnityEngine
{
	// The legacy (RakNet) networking, unused by the game's own multiplayer (UNet); only read in a few places.
	public enum NetworkDisconnection
	{
		LostConnection = 20,
		Disconnected = 19,
	}

	public struct NetworkPlayer
	{
		public string ipAddress => "127.0.0.1";
	}

	public struct NetworkMessageInfo
	{
		public double timestamp => Time.realtimeSinceStartupAsDouble;
	}

	public sealed class Network
	{
		public static double time => Time.realtimeSinceStartupAsDouble;

		public static NetworkPlayer player => default;
	}

	public sealed class BitStream
	{
		public bool isWriting => false;

		public void Serialize(ref Vector3 value)
		{
		}

		public void Serialize(ref Quaternion value)
		{
		}
	}

	// GUIText and GUITexture (removed in Unity 2019) as plain components; nothing draws them.
	public class GUIElement : MonoBehaviour
	{
		public bool HitTest(Vector3 screenPosition) => false;
	}

	public sealed class GUIText : GUIElement
	{
		public string text { get; set; }
		public Font font { get; set; }
		public TextAlignment alignment { get; set; }
	}

	public sealed class GUITexture : GUIElement
	{
		public Color color { get; set; }
		public Rect pixelInset { get; set; }
		public Texture texture { get; set; }
	}
}
