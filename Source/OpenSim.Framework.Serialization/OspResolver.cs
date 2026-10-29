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

// Adapted 2026 by Legion Builds: only the OSPA constants, MakeOspa(firstName, lastName) and HashName are carried. The methods
// that look a user up through the user account service are not, since oar-tools has no user service.

using System.Text;
using OpenMetaverse;

namespace OpenSim.Framework.Serialization;

/// <summary>
/// Resolves OpenSim Profile Anchors (OSPA).  An OSPA is a string used to provide information for
/// identifying user profiles or supplying a simple name if no profile is available.
/// </summary>
public class OspResolver
{
    public const string OSPA_PREFIX = "ospa:";
    public const string OSPA_NAME_KEY = "n";
    public const string OSPA_NAME_VALUE_SEPARATOR = " ";
    public const string OSPA_TUPLE_SEPARATOR = "|";
    public static readonly char[] OSPA_TUPLE_SEPARATOR_ARRAY = OSPA_TUPLE_SEPARATOR.ToCharArray();
    public const string OSPA_PAIR_SEPARATOR = "=";

    /// <summary>
    /// Make an OSPA given a user name
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    public static string MakeOspa(string firstName, string lastName)
    {
        string ospa
            = OSPA_PREFIX + OSPA_NAME_KEY + OSPA_PAIR_SEPARATOR + firstName + OSPA_NAME_VALUE_SEPARATOR + lastName;

        return ospa;
    }

    /// <summary>
    /// Hash a profile name into a UUID
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    public static UUID HashName(string name)
    {
        return new UUID(Utils.MD5(Encoding.Unicode.GetBytes(name)), 0);
    }
}
