using ImGuiNET;
using Microsoft.Xna.Framework;
using Vec2 = System.Numerics.Vector2;

namespace SWGLAnimationExtractor;

/// <summary>
/// Orbit camera (Quake axes: X forward, Y left, Z up). Independent from any viewer so that several
/// <see cref="SkeletonViewer"/>s can render the same view.
/// </summary>
public sealed class OrbitCamera
{
    public Vector3 Center;
    public float Radius = 40f;
    public float Yaw = 0.5f, Pitch = 0.1f, ZoomFactor = 1f;
    public Vector3 PanOffset;

    /// <summary>Frames the given bounding box and resets the orbit, pan and zoom.</summary>
    public void Fit(Vector3 min, Vector3 max)
    {
        Center = (min + max) / 2;
        Radius = Math.Max((max - min).Length() / 2, 1f);
        Yaw = 0.5f; Pitch = 0.1f; ZoomFactor = 1f; PanOffset = Vector3.Zero;
    }

    public void Orbit(Vec2 delta)
    {
        Yaw -= delta.X * 0.01f;
        Pitch = Math.Clamp(Pitch + delta.Y * 0.01f, -1.4f, 1.4f);
    }

    /// <summary>Moves the camera target in the screen plane.</summary>
    public void Pan(Vec2 delta)
    {
        var dir = Direction;
        var right = Vector3.Normalize(Vector3.Cross(-dir, Vector3.UnitZ));
        var up = Vector3.Cross(right, -dir);
        float scale = Radius * 2.6f * ZoomFactor * 0.0016f;
        PanOffset += (-right * delta.X + up * delta.Y) * scale;
    }

    public void Zoom(float wheel) => ZoomFactor = Math.Clamp(ZoomFactor * MathF.Pow(0.9f, wheel), 0.2f, 4f);

    /// <summary>Unit vector from the target to the eye.</summary>
    public Vector3 Direction =>
        new(MathF.Cos(Yaw) * MathF.Cos(Pitch), MathF.Sin(Yaw) * MathF.Cos(Pitch), MathF.Sin(Pitch));

    public Matrix View()
    {
        var target = Center + PanOffset;
        return Matrix.CreateLookAt(target + Direction * (Radius * 2.6f * ZoomFactor), target, Vector3.UnitZ);
    }

    public Matrix Projection(float aspect) =>
        Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(40), aspect, Math.Max(Radius * 0.05f, 0.1f), Radius * 20f);
}

/// <summary>ImGui input for an <see cref="OrbitCamera"/>: drag = orbit, right drag = pan, wheel = zoom.</summary>
public static class CameraInput
{
    /// <summary>Draws <paramref name="texture"/> and drives <paramref name="camera"/> from the mouse over it.</summary>
    public static void Image(IntPtr texture, Vec2 size, OrbitCamera camera)
    {
        var pos = ImGui.GetCursorScreenPos();
        ImGui.Image(texture, size);
        ImGui.SetCursorScreenPos(pos);
        ImGui.InvisibleButton("##viewport", size, ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight);
        Handle(camera);
    }

    /// <summary>Applies the mouse state to the camera for the last item (call right after it).</summary>
    public static void Handle(OrbitCamera camera)
    {
        var io = ImGui.GetIO();
        if (ImGui.IsItemActive())
        {
            if (ImGui.IsMouseDown(ImGuiMouseButton.Left)) camera.Orbit(io.MouseDelta);
            else if (ImGui.IsMouseDown(ImGuiMouseButton.Right)) camera.Pan(io.MouseDelta);
        }
        if (ImGui.IsItemHovered() && io.MouseWheel != 0) camera.Zoom(io.MouseWheel);
    }
}
