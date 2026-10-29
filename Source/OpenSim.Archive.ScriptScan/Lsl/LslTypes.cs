/*
 * Copyright (c) 2026 Legion Builds
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of Legion Builds nor the names of its contributors
 *       may be used to endorse or promote products derived from this software
 *       without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
 * AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
 * IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
 * ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE
 * LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR
 * CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF
 * SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS
 * INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN
 * CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
 * ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
 * POSSIBILITY OF SUCH DAMAGE.
 */

// The three LSL value types that the constants in LSL_Constants.cs are declared with. LSL_Constants.cs is copied from
// OpenSim.Region.ScriptEngine.Shared unchanged; the scanner reads its constants, so these types only have to hold a value
// and print it. The names are the ones the source uses, because LslConstants recognises a field by its type name.

namespace OpenSim.Region.ScriptEngine.Shared;

public static class LSL_Types
{
    /// <summary>An LSL integer.</summary>
    public readonly struct LSLInteger
    {
        public readonly int value;

        public LSLInteger(int value) { this.value = value; }

        public static implicit operator LSLInteger(int i) => new LSLInteger(i);

        public override string ToString() => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>An LSL vector.</summary>
    public sealed class Vector3
    {
        public readonly double x, y, z;

        public Vector3(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
    }

    /// <summary>An LSL rotation.</summary>
    public sealed class Quaternion
    {
        public readonly double x, y, z, s;

        public Quaternion(double x, double y, double z, double s) { this.x = x; this.y = y; this.z = z; this.s = s; }
    }
}
