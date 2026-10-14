using SentisRender.Net;

namespace SentisRender
{
    /// <summary>Copying and comparing a shape (the protocol's class stays plain: the mod compiles it too).</summary>
    internal static class ShapeDataEx
    {
        public static ShapeData Clone(this ShapeData d) => new ShapeData
        {
            Id = d.Id, Kind = d.Kind,
            X = d.X, Y = d.Y, Z = d.Z,
            ToX = d.ToX, ToY = d.ToY, ToZ = d.ToZ,
            Radius = d.Radius,
            SizeX = d.SizeX, SizeY = d.SizeY, SizeZ = d.SizeZ,
            ForwardX = d.ForwardX, ForwardY = d.ForwardY, ForwardZ = d.ForwardZ,
            UpX = d.UpX, UpY = d.UpY, UpZ = d.UpZ,
            Color = d.Color, Style = d.Style, Thickness = d.Thickness, Material = d.Material, Text = d.Text,
            LifetimeMs = d.LifetimeMs, DrawDistance = d.DrawDistance, Detail = d.Detail, Intensity = d.Intensity,
            Additive = d.Additive, WireColor = d.WireColor,
            SpinX = d.SpinX, SpinY = d.SpinY, SpinZ = d.SpinZ, SpinSpeed = d.SpinSpeed
        };

        public static bool SameAs(this ShapeData a, ShapeData b) =>
            a.Id == b.Id && a.Kind == b.Kind &&
            a.X == b.X && a.Y == b.Y && a.Z == b.Z &&
            a.ToX == b.ToX && a.ToY == b.ToY && a.ToZ == b.ToZ &&
            a.Radius == b.Radius &&
            a.SizeX == b.SizeX && a.SizeY == b.SizeY && a.SizeZ == b.SizeZ &&
            a.ForwardX == b.ForwardX && a.ForwardY == b.ForwardY && a.ForwardZ == b.ForwardZ &&
            a.UpX == b.UpX && a.UpY == b.UpY && a.UpZ == b.UpZ &&
            a.Color == b.Color && a.Style == b.Style && a.Thickness == b.Thickness && a.Material == b.Material &&
            a.Text == b.Text && a.LifetimeMs == b.LifetimeMs && a.DrawDistance == b.DrawDistance && a.Detail == b.Detail &&
            a.Intensity == b.Intensity && a.Additive == b.Additive && a.WireColor == b.WireColor &&
            a.SpinX == b.SpinX && a.SpinY == b.SpinY && a.SpinZ == b.SpinZ && a.SpinSpeed == b.SpinSpeed;
    }
}
