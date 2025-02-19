﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

// Source: https://github.com/dotnet/runtime/blob/53976d38b1bd6917b8fa4d1dd4f009728ece3adb/src/libraries/Common/src/System/Marvin.cs

using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace System.Formats.Cbor
{
    internal static class Marvin
    {
        /// <summary>
        /// Convenience method to compute a Marvin hash and collapse it into a 32-bit hash.
        /// </summary>
        public static int ComputeHash32(ReadOnlySpan<byte> data, ulong seed)
        {
            long hash64 = ComputeHash(data, seed);
            return ((int)(hash64 >> 32)) ^ (int)hash64;
        }

        /// <summary>
        /// Computes a 64-hash using the Marvin algorithm.
        /// </summary>
        public static long ComputeHash(ReadOnlySpan<byte> data, ulong seed)
        {
            uint p0 = (uint)seed;
            uint p1 = (uint)(seed >> 32);

            if (data.Length >= sizeof(uint))
            {
                ReadOnlySpan<uint> uData = MemoryMarshal.Cast<byte, uint>(data);

                for (int i = 0; i < uData.Length; i++)
                {
                    p0 += uData[i];
                    Block(ref p0, ref p1);
                }

                // byteOffset = data.Length - data.Length % 4
                // is equivalent to clearing last 2 bits of length
                // Using it directly gives a perf hit for short strings making it at least 5% or more slower.
                int byteOffset = data.Length & (~3);
                data = data.Slice(byteOffset);
            }

            switch (data.Length)
            {
                case 0:
                    p0 += 0x80u;
                    break;

                case 1:
                    p0 += 0x8000u | data[0];
                    break;

                case 2:
                    p0 += 0x800000u | MemoryMarshal.Cast<byte, ushort>(data)[0];
                    break;

                case 3:
                    p0 += 0x80000000u | (((uint)data[2]) << 16) | (uint)MemoryMarshal.Cast<byte, ushort>(data)[0];
                    break;

                default:
                    Debug.Fail("Should not get here.");
                    break;
            }

            Block(ref p0, ref p1);
            Block(ref p0, ref p1);

            return (((long)p1) << 32) | p0;
        }

        private static void Block(ref uint rp0, ref uint rp1)
        {
            uint p0 = rp0;
            uint p1 = rp1;

            p1 ^= p0;
            p0 = Rotl(p0, 20);

            p0 += p1;
            p1 = Rotl(p1, 9);

            p1 ^= p0;
            p0 = Rotl(p0, 27);

            p0 += p1;
            p1 = Rotl(p1, 19);

            rp0 = p0;
            rp1 = p1;
        }

        private static uint Rotl(uint value, int shift) =>
            // This is expected to be optimized into a single rol (or ror with negated shift value) instruction
            (value << shift) | (value >> (32 - shift));

        public static ulong DefaultSeed { get; } = GenerateSeed();

        private static ulong GenerateSeed()
        {
            using (var rng = RandomNumberGenerator.Create())
            {
                byte[] bytes = new byte[sizeof(ulong)];
                rng.GetBytes(bytes);
                return BinaryPrimitives.ReadUInt64LittleEndian(bytes);
            }
        }
    }
}
