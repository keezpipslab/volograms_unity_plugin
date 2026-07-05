// SharedAtlasVologramGroup.cs
// Wires 4 independent-geometry VolPlayers to read from ONE VolAtlasVideoDriver's
// decoded texture, each sampling its own quadrant via material UV tiling/offset.
//
// SETUP:
//   1. Put a VolAtlasVideoDriver on some GameObject, pointing at your packed
//      2x2 atlas mp4.
//   2. Create your 4 vologram GameObjects as normal (MeshFilter, MeshRenderer,
//      VolPlayer, volFormat = Video, each pointing at its own header/sequence
//      folder). Leave volVideoTexture EMPTY on these -- they won't use it.
//   3. IMPORTANT: disable these 4 GameObjects in the scene (uncheck the
//      active checkbox). This controller re-enables them itself, after the
//      shared texture is ready, so their Start()/Open() never races the driver.
//   4. Assign the driver + the 4 slots below, including each slot's quadrant
//      and its OWN material instance (not a shared material asset).

using System;
using System.Collections;
using UnityEngine;
using Volograms;

public class SharedAtlasVologramGroup : MonoBehaviour
{
    public enum Quadrant { TopLeft, TopRight, BottomLeft, BottomRight }

    [Serializable]
    public class Slot
    {
        public string label = "vologram";
        public GameObject vologramObject;   // starts disabled in the scene
        public VolPlayer player;            // on vologramObject
        public Material material;           // instance on vologramObject's renderer
        public Quadrant quadrant;
    }

    public VolAtlasVideoDriver driver;
    public Slot[] slots = new Slot[4];

    private IEnumerator Start()
    {
        // Wait until the driver has opened the atlas video and created its texture.
        yield return new WaitUntil(() => driver != null && driver.IsReady);

        foreach (var slot in slots)
        {
            if (slot == null || slot.player == null || slot.vologramObject == null) continue;

            // Wire the shared texture in BEFORE this object's Start()/Open() runs.
            slot.player.useSharedVideoTexture = true;
            slot.player.sharedVideoTexture = driver.AtlasTexture;

            if (slot.material != null)
            {
                ApplyQuadrant(slot.material, slot.quadrant);
                slot.player.material = slot.material;
            }

            // Now safe to activate -- Start()/Open() runs with everything in place.
            slot.vologramObject.SetActive(true);
        }
    }

    private static void ApplyQuadrant(Material material, Quadrant quadrant)
    {
        Vector2 scale = new Vector2(0.5f, 0.5f);
        Vector2 offset = quadrant switch
        {
            Quadrant.TopLeft => new Vector2(0f, 0.5f),
            Quadrant.TopRight => new Vector2(0.5f, 0.5f),
            Quadrant.BottomLeft => new Vector2(0f, 0f),
            Quadrant.BottomRight => new Vector2(0.5f, 0f),
            _ => Vector2.zero
        };
        material.mainTextureScale = scale;
        material.mainTextureOffset = offset;
    }
}
