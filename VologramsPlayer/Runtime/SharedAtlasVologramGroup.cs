// SharedAtlasVologramGroup.cs
// Wires 4 independent-geometry VolPlayers to read from ONE VolAtlasVideoDriver's
// decoded texture, each sampling its own quadrant via material UV tiling/offset.
//
// SETUP:
//   1. Put a VolAtlasVideoDriver on some GameObject, pointing at your packed
//      2x2 atlas mp4.
//   2. Create your 4 vologram GameObjects as normal (MeshFilter, MeshRenderer,
//      VolPlayer, volFormat = Video, each pointing at its own header/sequence
//      folder). Leave volVideoTexture EMPTY -- they won't use it.
//   3. On each of the 4 VolPlayers, tick "Use Shared Video Texture" in the
//      Inspector. Leave "Shared Video Texture" empty -- this controller fills
//      it in at runtime. All 4 objects can stay active/enabled as normal;
//      VolPlayer's Open() now waits internally for this field to be assigned,
//      so Start() call order no longer matters.
//   4. Assign the driver + the 4 slots below, including each slot's quadrant
//      and its OWN material instance (not a shared material asset).
public class SharedAtlasVologramGroup : UnityEngine.MonoBehaviour
{
    [System.Serializable]
    public class Slot
    {
        public string label = "vologram";
        public Volograms.VolPlayer player;
        public UnityEngine.Material material; // instance on the player's renderer
        public Quadrant quadrant;
    }

    public enum Quadrant { TopLeft, TopRight, BottomLeft, BottomRight }

    public VolAtlasVideoDriver driver;
    public Slot[] slots = new Slot[4];

    [UnityEngine.Tooltip("The native decoder reads frames vertically flipped, so the atlas's authored top half lands in the bottom half of the uploaded texture. Leave this ON to correct for that. If your quadrants still look swapped top/bottom, turn it off instead.")]
    public bool compensateForVideoFlip = true;

    private System.Collections.IEnumerator Start()
    {
        // Wait until the driver has opened the atlas video and created its texture.
        yield return new UnityEngine.WaitUntil(() => driver != null && driver.IsReady);

        foreach (var slot in slots)
        {
            if (slot == null || slot.player == null) continue;

            // The follower's own Open() coroutine is already waiting on this
            // field internally, so simple assignment is enough regardless of
            // whether its Start() ran before or after this one.
            slot.player.sharedVideoTexture = driver.AtlasTexture;

            if (slot.material != null)
            {
                ApplyQuadrant(slot.material, slot.quadrant, compensateForVideoFlip);
                slot.player.material = slot.material;
            }
        }
    }

    private static void ApplyQuadrant(UnityEngine.Material material, Quadrant quadrant, bool compensateForFlip)
    {
        bool isTop = quadrant == Quadrant.TopLeft || quadrant == Quadrant.TopRight;
        bool isLeft = quadrant == Quadrant.TopLeft || quadrant == Quadrant.BottomLeft;

        // Standard (unflipped) UV convention: V=0 is bottom, V=1 is top, so
        // "top" quadrants get offset 0.5. If the decoded texture is vertically
        // flipped relative to how the atlas was authored, invert which half
        // counts as "top".
        float y = (isTop != compensateForFlip) ? 0.5f : 0f;
        float x = isLeft ? 0f : 0.5f;

        material.mainTextureScale = new UnityEngine.Vector2(0.5f, 0.5f);
        material.mainTextureOffset = new UnityEngine.Vector2(x, y);
    }
}
