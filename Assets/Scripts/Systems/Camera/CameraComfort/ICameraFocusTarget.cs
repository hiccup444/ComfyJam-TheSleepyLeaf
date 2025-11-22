using UnityEngine;

namespace Comfy.Camera
{
    /// <summary>
    /// Optional marker for focusable gameplay objects. Implement on draggables or anchors to help the comfort rig
    /// nudge the camera toward the active interaction.
    /// </summary>
    public interface ICameraFocusTarget
    {
        /// <summary>
        /// Returns the world-space bounds the camera should consider when auto focusing.
        /// </summary>
        Bounds GetFocusBounds();
    }
}
