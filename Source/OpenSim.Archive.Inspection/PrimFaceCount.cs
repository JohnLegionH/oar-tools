/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSimulator Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

// Adapted 2026 by Legion Builds from OpenSim.Region.Framework/Scenes/SceneObjectPart.cs (GetNumberOfSides, GetPrimType and
// the PrimType enum). The two methods are the region server's own rule for the number of faces of a prim; they read a
// PrimitiveBaseShape instead of the shape of a scene object part, so no scene is needed. The bodies are otherwise as in
// the source.

using System.Xml.Linq;
using OpenMetaverse;
using OpenSim.Framework;

namespace OpenSim.Archive.Inspection;

/// <summary>The kinds of prim the face rule tells apart.</summary>
public enum PrimType : int
{
    BOX = 0,
    CYLINDER = 1,
    PRISM = 2,
    SPHERE = 3,
    TORUS = 4,
    TUBE = 5,
    RING = 6,
    SCULPT = 7
}

/// <summary>The number of faces the region server gives a prim shape (what llGetNumberOfSides returns).</summary>
public static class PrimFaceCount
{
    /// <summary>Get the number of sides that a part of this shape has.</summary>
    public static int GetNumberOfSides(PrimitiveBaseShape Shape)
    {
        int ret = 0;
        int cut;

        if (Shape.SculptEntry)
        {
            if (Shape.SculptType != (byte)SculptType.Mesh)
                return 1; // sculp

            //hack to detect new upload with faces data enconded on pbs              
            if ((Shape.ProfileCurve & 0xf0) != (byte)HollowShape.Triangle)
                // old broken upload TODO
                return 8;
        }

        PrimType primType = GetPrimType(Shape, true);

        switch (primType)
        {
            case PrimType.BOX:
                ret = 6;
                if (Shape.ProfileBegin > 0 || Shape.ProfileEnd > 0) // cut case
                {
                    // removed sides
                    if (Shape.ProfileBegin > 0)
                    {
                        cut = Shape.ProfileBegin;
                        cut /= 12500;
                        ret -= cut;
                    }
                    if (Shape.ProfileEnd > 0)
                    {
                        cut = Shape.ProfileEnd;
                        cut /= 12500;
                        ret -= cut;
                    }
                    ret += 2;
                }
                break;
            case PrimType.PRISM:
                ret = 5;
                if (Shape.ProfileBegin > 0 || Shape.ProfileEnd > 0) // cut case
                {
                    // removed sides
                    if (Shape.ProfileBegin > 0)
                    {
                        cut = (Shape.ProfileBegin);
                        if (cut >= 16667)
                            ret--;
                        if (cut >= 33333)
                            ret--;
                    }
                    if (Shape.ProfileEnd > 0)
                    {
                        cut = (Shape.ProfileEnd);
                        if (cut >= 16667)
                            ret--;
                        if (cut >= 33333)
                            ret--;
                    }
                    ret += 2; // both cut faces
                }
                break;
            case PrimType.CYLINDER:
                ret = 3;
                if (Shape.ProfileBegin > 0 || Shape.ProfileEnd > 0) // cut
                    ret += 2;
                break;
            case PrimType.SPHERE:
                ret = 1;
                // cut faces exist if cut or skew or unequal twist limits
                if (Shape.PathBegin > 0 || Shape.PathEnd > 0 || Shape.PathSkew != 0 || (Shape.PathTwistBegin != Shape.PathTwist))
                    ret += 2;
                if (Shape.ProfileBegin > 0 || Shape.ProfileEnd > 0 || Shape.ProfileHollow > 0) // dimple also if hollow
                    ret += 2;
                break;
            case PrimType.TORUS:
                ret = 1;
                if (Shape.PathBegin > 0 || Shape.PathEnd > 0 || Shape.PathSkew != 0
                        || Shape.PathTaperX != 0 || Shape.PathTaperY != 0 || Shape.PathRevolutions > 0
                        || Shape.PathRadiusOffset != 0 || (Shape.PathTwistBegin != Shape.PathTwist))
                    ret += 2;
                if (Shape.ProfileBegin > 0 || Shape.ProfileEnd > 0) // profile cut
                    ret += 2;
                break;
            case PrimType.TUBE:
                ret = 4;
                if (Shape.PathBegin > 0 || Shape.PathEnd > 0 || Shape.PathSkew != 0
                        || Shape.PathTaperX != 0 || Shape.PathTaperY != 0 || Shape.PathRevolutions > 0
                        || Shape.PathRadiusOffset != 0 || (Shape.PathTwistBegin != Shape.PathTwist))
                    ret += 2;
                if (Shape.ProfileBegin > 0 || Shape.ProfileEnd > 0) // profile cut
                {
                    if (Shape.ProfileBegin > 0)
                    {
                        cut = Shape.ProfileBegin;
                        cut /= 12500;
                        ret -= cut;
                    }
                    if (Shape.ProfileEnd > 0)
                    {
                        cut = Shape.ProfileEnd;
                        cut /= 12500;
                        ret -= cut;
                    }
                    ret += 2;
                }
                break;
            case PrimType.RING:
                ret = 3;
                if (Shape.PathBegin > 0 || Shape.PathEnd > 0 || Shape.PathSkew != 0
                        || Shape.PathTaperX != 0 || Shape.PathTaperY != 0 || Shape.PathRevolutions > 0
                        || Shape.PathRadiusOffset != 0 || (Shape.PathTwistBegin != Shape.PathTwist))
                    ret += 2;
                if (Shape.ProfileBegin > 0 || Shape.ProfileEnd > 0) // profile cut
                {
                    if (Shape.ProfileBegin > 0)
                    {
                        cut = Shape.ProfileBegin;
                        if (cut >= 16667)
                            ret--;
                        if (cut >= 33333)
                            ret--;
                    }
                    if (Shape.ProfileEnd > 0)
                    {
                        cut = Shape.ProfileEnd;
                        if (cut >= 16667)
                            ret--;
                        if (cut >= 33333)
                            ret--;
                    }
                    ret += 2;
                }
                break;
        }

        // hollow face commum to all
        if (Shape.ProfileHollow > 0)
            ret += 1;

        return ret;
    }

    /// <summary>Tell us what type a prim of this shape is.</summary>
    public static PrimType GetPrimType(PrimitiveBaseShape Shape, bool ignoreSculpt = false)
    {
        if (Shape.SculptEntry && !ignoreSculpt)
            return PrimType.SCULPT;
        ProfileShape ps = (ProfileShape)(Shape.ProfileCurve & 0x07);
        if (ps == ProfileShape.Square)
        {
            if (Shape.PathCurve == (byte)Extrusion.Straight || Shape.PathCurve == (byte)Extrusion.Flexible)
                return PrimType.BOX;
            else if (Shape.PathCurve == (byte)Extrusion.Curve1)
                return PrimType.TUBE;
        }
        else if (ps == ProfileShape.Circle)
        {
            if (Shape.PathCurve == (byte)Extrusion.Straight || Shape.PathCurve == (byte)Extrusion.Flexible)
                return PrimType.CYLINDER;
            // ProfileCurve seems to combine hole shape and profile curve so we need to only compare against the lower 3 bits
            else if (Shape.PathCurve == (byte)Extrusion.Curve1)
                return PrimType.TORUS;
        }
        else if (ps == ProfileShape.HalfCircle)
        {
            if (Shape.PathCurve == (byte)Extrusion.Curve1 || Shape.PathCurve == (byte)Extrusion.Curve2)
                return PrimType.SPHERE;
        }
        else if (ps == ProfileShape.EquilateralTriangle)
        {
            if (Shape.PathCurve == (byte)Extrusion.Straight || Shape.PathCurve == (byte)Extrusion.Flexible)
                return PrimType.PRISM;
            else if (Shape.PathCurve == (byte)Extrusion.Curve1)
                return PrimType.RING;
        }

        return PrimType.BOX;
    }

    /// <summary>
    /// The shape fields of a stored <c>Shape</c> element, read the way the region server's object reader and
    /// <see cref="ObjectXmlParser"/> read them. A field the element does not carry stays 0, as in a new shape.
    /// </summary>
    public static ShapeData ShapeFromXml(XElement shape)
    {
        ShapeData sd = new ShapeData();
        if (shape == null)
            return sd;
        foreach (XElement e in shape.Elements())
        {
            string v = e.Value;
            switch (e.Name.LocalName)
            {
                case "PCode": sd.PCode = Int(v); break;
                case "State": sd.State = Int(v); break;
                case "ProfileCurve": sd.ProfileCurve = Int(v); break;
                case "ProfileShape": sd.ProfileShapeName = v; break;
                case "HollowShape": sd.HollowShapeName = v; break;
                case "PathCurve": sd.PathCurve = Int(v); break;
                case "PathBegin": sd.PathBegin = Int(v); break;
                case "PathEnd": sd.PathEnd = Int(v); break;
                case "PathScaleX": sd.PathScaleX = Int(v); break;
                case "PathScaleY": sd.PathScaleY = Int(v); break;
                case "PathShearX": sd.PathShearX = Int(v); break;
                case "PathShearY": sd.PathShearY = Int(v); break;
                case "PathTwist": sd.PathTwist = Int(v); break;
                case "PathTwistBegin": sd.PathTwistBegin = Int(v); break;
                case "PathRadiusOffset": sd.PathRadiusOffset = Int(v); break;
                case "PathRevolutions": sd.PathRevolutions = Int(v); break;
                case "PathTaperX": sd.PathTaperX = Int(v); break;
                case "PathTaperY": sd.PathTaperY = Int(v); break;
                case "PathSkew": sd.PathSkew = Int(v); break;
                case "ProfileBegin": sd.ProfileBegin = Int(v); break;
                case "ProfileEnd": sd.ProfileEnd = Int(v); break;
                case "ProfileHollow": sd.ProfileHollow = Int(v); break;
                case "SculptType": sd.SculptType = Int(v); break;
                case "SculptEntry": sd.SculptEntry = string.Equals(v, "true", StringComparison.OrdinalIgnoreCase); break;
                case "SculptTexture": UUID.TryParse(e.Element("Guid")?.Value ?? e.Element("UUID")?.Value ?? v, out sd.SculptTexture); break;
            }
        }
        return sd;
    }

    private static int Int(string s) =>
        int.TryParse(s?.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int v) ? v : 0;

    /// <summary>
    /// Builds the shape object the region server would hold for the stored fields, with the same text-form override the
    /// loader applies (a stored <c>ProfileShape</c> or <c>HollowShape</c> name replaces the matching bits of ProfileCurve).
    /// </summary>
    public static PrimitiveBaseShape ToServerShape(ShapeData s)
    {
        PrimitiveBaseShape p = new PrimitiveBaseShape();
        p.ProfileCurve = unchecked((byte)s.ProfileCurve);
        if (!string.IsNullOrEmpty(s.ProfileShapeName) && Enum.TryParse(s.ProfileShapeName, true, out ProfileShape ps))
            p.ProfileShape = ps;
        if (!string.IsNullOrEmpty(s.HollowShapeName) && Enum.TryParse(s.HollowShapeName, true, out HollowShape hs))
            p.HollowShape = hs;
        p.PathCurve = unchecked((byte)s.PathCurve);
        p.PathBegin = unchecked((ushort)s.PathBegin);
        p.PathEnd = unchecked((ushort)s.PathEnd);
        p.PathScaleX = unchecked((byte)s.PathScaleX);
        p.PathScaleY = unchecked((byte)s.PathScaleY);
        p.PathShearX = unchecked((byte)s.PathShearX);
        p.PathShearY = unchecked((byte)s.PathShearY);
        p.PathTwist = unchecked((sbyte)s.PathTwist);
        p.PathTwistBegin = unchecked((sbyte)s.PathTwistBegin);
        p.PathRadiusOffset = unchecked((sbyte)s.PathRadiusOffset);
        p.PathRevolutions = unchecked((byte)s.PathRevolutions);
        p.PathTaperX = unchecked((sbyte)s.PathTaperX);
        p.PathTaperY = unchecked((sbyte)s.PathTaperY);
        p.PathSkew = unchecked((sbyte)s.PathSkew);
        p.ProfileBegin = unchecked((ushort)s.ProfileBegin);
        p.ProfileEnd = unchecked((ushort)s.ProfileEnd);
        p.ProfileHollow = unchecked((ushort)s.ProfileHollow);
        p.PCode = unchecked((byte)s.PCode);
        p.SculptEntry = s.SculptEntry;
        p.SculptType = unchecked((byte)s.SculptType);
        p.SculptTexture = s.SculptTexture;
        return p;
    }
}
