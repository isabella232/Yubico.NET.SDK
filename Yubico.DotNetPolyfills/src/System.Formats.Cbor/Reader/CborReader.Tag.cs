﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

// Source: https://github.com/dotnet/runtime/tree/v5.0.0-preview.7.20364.11/src/libraries/System.Formats.Cbor/src/System/Formats/Cbor

using System;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Collections.Generic;

namespace System.Formats.Cbor
{
    public partial class CborReader
    {
        /// <summary>
        ///   Reads the next data item as a semantic tag (major type 6).
        /// </summary>
        /// <returns>The decoded value.</returns>
        /// <exception cref="InvalidOperationException">
        ///   the next data item does not have the correct major type.
        /// </exception>
        /// <exception cref="CborContentException">
        ///   the next value has an invalid CBOR encoding. -or-
        ///   there was an unexpected end of CBOR encoding data. -or-
        ///   the next value uses a CBOR encoding that is not valid under the current conformance mode.
        /// </exception>
        [CLSCompliant(false)] // Imported from future System.Formats.Cbor
        public CborTag ReadTag()
        {
            CborTag tag = PeekTagCore(out int bytesRead);

            AdvanceBuffer(bytesRead);
            _isTagContext = true;
            return tag;
        }

        /// <summary>
        ///   Reads the next data item as a semantic tag (major type 6),
        ///   without advancing the reader.
        /// </summary>
        /// <returns>The decoded value.</returns>
        /// <exception cref="InvalidOperationException">
        ///   the next data item does not have the correct major type.
        /// </exception>
        /// <exception cref="CborContentException">
        ///   the next value has an invalid CBOR encoding. -or-
        ///   there was an unexpected end of CBOR encoding data. -or-
        ///   the next value uses a CBOR encoding that is not valid under the current conformance mode.
        /// </exception>
        /// <remarks>
        ///   Useful in scenaria where the semantic value decoder needs to be determined at runtime.
        /// </remarks>
        [CLSCompliant(false)] // Imported from future System.Formats.Cbor
        public CborTag PeekTag() => PeekTagCore(out int _);

        /// <summary>
        ///   Reads the next data item as a tagged date/time string,
        ///   as described in RFC7049 section 2.4.1.
        /// </summary>
        /// <returns>The decoded value.</returns>
        /// <exception cref="InvalidOperationException">
        ///   the next data item does not have the correct major type. -or-
        ///   the next date item does not have the correct semantic tag.
        /// </exception>
        /// <exception cref="CborContentException">
        ///   the next value has an invalid CBOR encoding. -or-
        ///   there was an unexpected end of CBOR encoding data. -or-
        ///   invalid semantic date/time encoding. -or-
        ///   the next value uses a CBOR encoding that is not valid under the current conformance mode.
        /// </exception>
        public DateTimeOffset ReadDateTimeOffset()
        {
            // implements https://tools.ietf.org/html/rfc7049#section-2.4.1

            Checkpoint checkpoint = CreateCheckpoint();

            try
            {
                ReadExpectedTag(expectedTag: CborTag.DateTimeString);

                switch (PeekState())
                {
                    case CborReaderState.TextString:
                    case CborReaderState.StartIndefiniteLengthTextString:
                        break;
                    default:
                        throw new CborContentException(CborExceptionMessages.Cbor_Reader_InvalidDateTimeEncoding);
                }

                string dateString = ReadTextString();

                if (!DateTimeOffset.TryParseExact(dateString, CborWriter.Rfc3339FormatString, null, DateTimeStyles.RoundtripKind, out DateTimeOffset result))
                {
                    throw new CborContentException(CborExceptionMessages.Cbor_Reader_InvalidDateTimeEncoding);
                }

                return result;
            }
            catch
            {
                RestoreCheckpoint(in checkpoint);
                throw;
            }
        }

        /// <summary>
        ///   Reads the next data item as a tagged unix time in seconds,
        ///   as described in RFC7049 section 2.4.1.
        /// </summary>
        /// <returns>The decoded value.</returns>
        /// <exception cref="InvalidOperationException">
        ///   the next data item does not have the correct major type. -or-
        ///   the next date item does not have the correct semantic tag.
        /// </exception>
        /// <exception cref="CborContentException">
        ///   the next value has an invalid CBOR encoding. -or-
        ///   there was an unexpected end of CBOR encoding data. -or-
        ///   invalid semantic date/time encoding. -or-
        ///   the next value uses a CBOR encoding that is not valid under the current conformance mode.
        /// </exception>
        public DateTimeOffset ReadUnixTimeSeconds()
        {
            // implements https://tools.ietf.org/html/rfc7049#section-2.4.1

            Checkpoint checkpoint = CreateCheckpoint();

            try
            {
                ReadExpectedTag(expectedTag: CborTag.UnixTimeSeconds);

                switch (PeekState())
                {
                    case CborReaderState.UnsignedInteger:
                    case CborReaderState.NegativeInteger:
                        return DateTimeOffset.FromUnixTimeSeconds(ReadInt64());

                    case CborReaderState.HalfPrecisionFloat:
                    case CborReaderState.SinglePrecisionFloat:
                    case CborReaderState.DoublePrecisionFloat:
                        double seconds = ReadDouble();

                        if (double.IsNaN(seconds) || double.IsInfinity(seconds))
                        {
                            throw new CborContentException(CborExceptionMessages.Cbor_Reader_InvalidUnixTimeEncoding);
                        }

                        var timespan = TimeSpan.FromSeconds(seconds);
                        return DateTimeOffset.FromUnixTimeSeconds(0).Add(timespan);

                    default:
                        throw new CborContentException(CborExceptionMessages.Cbor_Reader_InvalidUnixTimeEncoding);
                }
            }
            catch
            {
                RestoreCheckpoint(in checkpoint);
                throw;
            }
        }

        /// <summary>
        ///   Reads the next data item as a tagged bignum encoding,
        ///   as described in RFC7049 section 2.4.2.
        /// </summary>
        /// <returns>The decoded value.</returns>
        /// <exception cref="InvalidOperationException">
        ///   the next data item does not have the correct major type. -or-
        ///   the next date item does not have the correct semantic tag.
        /// </exception>
        /// <exception cref="CborContentException">
        ///   the next value has an invalid CBOR encoding. -or-
        ///   there was an unexpected end of CBOR encoding data. -or-
        ///   invalid semantic bignum encoding. -or-
        ///   the next value uses a CBOR encoding that is not valid under the current conformance mode.
        /// </exception>
        public BigInteger ReadBigInteger()
        {
            // implements https://tools.ietf.org/html/rfc7049#section-2.4.2

            Checkpoint checkpoint = CreateCheckpoint();

            try
            {
                bool isNegative = ReadTag() switch
                {
                    CborTag.UnsignedBigNum => false,
                    CborTag.NegativeBigNum => true,
                    _ => throw new InvalidOperationException(CborExceptionMessages.Cbor_Reader_InvalidBigNumEncoding),
                };

                switch (PeekState())
                {
                    case CborReaderState.ByteString:
                    case CborReaderState.StartIndefiniteLengthByteString:
                        break;
                    default:
                        throw new CborContentException(CborExceptionMessages.Cbor_Reader_InvalidBigNumEncoding);
                }

                byte[] unsignedBigEndianEncoding = ReadByteString();

                // NOTE: We replicate the .netstandard2.1 behavior for (isUnsigned: true, isBigEndian: true)
                IEnumerable<byte> unsignedLittleEndianEncoding = unsignedBigEndianEncoding.Reverse();

                // If the high bit is set, we need to add padding byte
                if ((unsignedBigEndianEncoding.Length > 0) && ((unsignedBigEndianEncoding[^1] & 0x80) != 0)) {
                    unsignedLittleEndianEncoding = unsignedLittleEndianEncoding.Concat(new byte[1]);
                }
                
                var unsignedValue = new BigInteger(unsignedLittleEndianEncoding.ToArray());
                return isNegative ? -1 - unsignedValue : unsignedValue;
            }
            catch
            {
                RestoreCheckpoint(in checkpoint);
                throw;
            }
        }

        /// <summary>
        ///   Reads the next data item as a tagged decimal fraction encoding,
        ///   as described in RFC7049 section 2.4.3.
        /// </summary>
        /// <returns>The decoded value.</returns>
        /// <exception cref="InvalidOperationException">
        ///   the next data item does not have the correct major type. -or-
        ///   the next date item does not have the correct semantic tag.
        /// </exception>
        /// <exception cref="OverflowException">
        ///   Decoded decimal fraction is either too large or too small for a <see cref="decimal"/> value.
        /// </exception>
        /// <exception cref="CborContentException">
        ///   the next value has an invalid CBOR encoding. -or-
        ///   there was an unexpected end of CBOR encoding data. -or-
        ///   invalid semantic decimal fraction encoding. -or-
        ///   the next value uses a CBOR encoding that is not valid under the current conformance mode.
        /// </exception>
        public decimal ReadDecimal()
        {
            // implements https://tools.ietf.org/html/rfc7049#section-2.4.3

            Checkpoint checkpoint = CreateCheckpoint();

            try
            {
                ReadExpectedTag(expectedTag: CborTag.DecimalFraction);

                if (PeekState() != CborReaderState.StartArray || ReadStartArray() != 2)
                {
                    throw new CborContentException(CborExceptionMessages.Cbor_Reader_InvalidDecimalEncoding);
                }

                decimal mantissa; // signed integral component of the decimal value
                long exponent;    // base-10 exponent

                switch (PeekState())
                {
                    case CborReaderState.UnsignedInteger:
                    case CborReaderState.NegativeInteger:
                        exponent = ReadInt64();
                        break;

                    default:
                        throw new CborContentException(CborExceptionMessages.Cbor_Reader_InvalidDecimalEncoding);
                }

                switch (PeekState())
                {
                    case CborReaderState.UnsignedInteger:
                        mantissa = ReadUInt64();
                        break;

                    case CborReaderState.NegativeInteger:
                        mantissa = -1m - ReadCborNegativeIntegerRepresentation();
                        break;

                    case CborReaderState.Tag:
                        switch (PeekTag())
                        {
                            case CborTag.UnsignedBigNum:
                            case CborTag.NegativeBigNum:
                                mantissa = (decimal)ReadBigInteger();
                                break;

                            default:
                                throw new CborContentException(CborExceptionMessages.Cbor_Reader_InvalidDecimalEncoding);
                        }

                        break;

                    default:
                        throw new CborContentException(CborExceptionMessages.Cbor_Reader_InvalidDecimalEncoding);
                }

                ReadEndArray();

                return CborWriter.DecimalHelpers.Reconstruct(mantissa, exponent);
            }
            catch
            {
                RestoreCheckpoint(in checkpoint);
                throw;
            }
        }

        private void ReadExpectedTag(CborTag expectedTag)
        {
            CborTag tag = PeekTagCore(out int bytesRead);

            if (expectedTag != tag)
            {
                throw new InvalidOperationException(CborExceptionMessages.Cbor_Reader_TagMismatch);
            }

            AdvanceBuffer(bytesRead);
            _isTagContext = true;
        }

        private CborTag PeekTagCore(out int bytesRead)
        {
            CborInitialByte header = PeekInitialByte(expectedType: CborMajorType.Tag);
            var result = (CborTag)DecodeUnsignedInteger(header, GetRemainingBytes(), out bytesRead);

            if (_isConformanceModeCheckEnabled && !CborConformanceModeHelpers.AllowsTags(ConformanceMode))
            {
                throw new CborContentException(string.Format(CultureInfo.CurrentCulture, CborExceptionMessages.Cbor_ConformanceMode_TagsNotSupported, ConformanceMode));
            }

            return result;
        }
    }
}
