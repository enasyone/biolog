using System.Text;

namespace Biolog.Features.NfcReader.Model;

/// <summary>
/// The seed input for NFC chip access-key derivation (BIO-DOC-007-AC01: "NFC access keys are
/// derived only from permitted document data/user interaction"). Two variants, matching the two
/// legitimate ICAO 9303 Part 11 access-control sources the AC's own wording names:
///
/// <list type="bullet">
/// <item><see cref="Mrz"/> - "permitted document data": the printed MRZ's document number, date
/// of birth and date of expiry, each with its own check digit (Basic Access Control's seed
/// inputs). Structurally this type carries nothing beyond those three fields - no name, no
/// nationality, no photo - so "derived only from permitted data" is enforced by what the type
/// CAN hold, not by convention.</item>
/// <item><see cref="CardAccessNumber"/> - "user interaction": a 6-digit Card Access Number the
/// user reads off the document and enters directly (PACE's CAN-based access mode) - proof of
/// physical possession without needing to re-key the whole MRZ.</item>
/// </list>
///
/// <para>
/// Both variants feed the identical SHA-1 seed/KDF machinery in <see cref="ChipAccessKeyDerivation"/>
/// (ICAO 9303 Part 11 Appendix D.1 defines one shared key-derivation function BAC and PACE both
/// use) - only <see cref="SeedInputBytes"/>, the byte string that gets hashed, differs between them.
/// </para>
///
/// <para>
/// This story does not implement PACE's Diffie-Hellman/ECDH key-agreement handshake or BAC's
/// challenge-response mutual authentication - those are chip-negotiation protocol steps that need
/// an actual chip to talk to (see <see cref="IChipDataGroupTransport.EstablishSecureChannel"/>'s
/// remarks). This type only covers the access-key-material INPUT side both protocols share.
/// </para>
/// </summary>
public abstract record NfcAccessKeySeedMaterial
{
    private NfcAccessKeySeedMaterial() { }

    /// <summary>The exact ASCII byte sequence <see cref="ChipAccessKeyDerivation.DeriveKeySeed"/>
    /// hashes - never exposed as a settable property, only ever computed from this record's own
    /// validated fields.</summary>
    public abstract byte[] SeedInputBytes { get; }

    /// <summary>ICAO 9303 Part 11 Basic Access Control's key-seed source: MRZ document number,
    /// date of birth and date of expiry, each with its ICAO check digit. Check digits are taken as
    /// already-computed input (this story does not depend on BIO-DOC-002's MRZ engine to compute
    /// them - the same "plain domain parameter, not a resolved dependency" pattern BIO-DOC-005's
    /// <c>ParserCaller</c> used).</summary>
    public sealed record Mrz : NfcAccessKeySeedMaterial
    {
        public string DocumentNumber { get; }
        public char DocumentNumberCheckDigit { get; }
        public string DateOfBirth { get; }
        public char DateOfBirthCheckDigit { get; }
        public string DateOfExpiry { get; }
        public char DateOfExpiryCheckDigit { get; }

        private Mrz(
            string documentNumber, char documentNumberCheckDigit,
            string dateOfBirth, char dateOfBirthCheckDigit,
            string dateOfExpiry, char dateOfExpiryCheckDigit)
        {
            DocumentNumber = documentNumber;
            DocumentNumberCheckDigit = documentNumberCheckDigit;
            DateOfBirth = dateOfBirth;
            DateOfBirthCheckDigit = dateOfBirthCheckDigit;
            DateOfExpiry = dateOfExpiry;
            DateOfExpiryCheckDigit = dateOfExpiryCheckDigit;
        }

        /// <summary>Validates ICAO 9303's own field shapes (document number up to 9 characters,
        /// dates exactly 6 digits YYMMDD, check digits from ICAO's check-digit alphabet
        /// <c>0-9</c>/<c>&lt;</c>) before constructing - a malformed value is rejected here, never
        /// silently truncated or padded into something that merely compiles.</summary>
        public static Mrz Create(
            string documentNumber, char documentNumberCheckDigit,
            string dateOfBirth, char dateOfBirthCheckDigit,
            string dateOfExpiry, char dateOfExpiryCheckDigit)
        {
            if (string.IsNullOrEmpty(documentNumber) || documentNumber.Length > 9)
            {
                throw new ArgumentException("document number must be 1-9 characters", nameof(documentNumber));
            }

            if (!IsSixDigitDate(dateOfBirth))
            {
                throw new ArgumentException("date of birth must be exactly 6 digits (YYMMDD)", nameof(dateOfBirth));
            }

            if (!IsSixDigitDate(dateOfExpiry))
            {
                throw new ArgumentException("date of expiry must be exactly 6 digits (YYMMDD)", nameof(dateOfExpiry));
            }

            RequireCheckDigitAlphabet(documentNumberCheckDigit, nameof(documentNumberCheckDigit));
            RequireCheckDigitAlphabet(dateOfBirthCheckDigit, nameof(dateOfBirthCheckDigit));
            RequireCheckDigitAlphabet(dateOfExpiryCheckDigit, nameof(dateOfExpiryCheckDigit));

            return new Mrz(documentNumber, documentNumberCheckDigit, dateOfBirth, dateOfBirthCheckDigit, dateOfExpiry, dateOfExpiryCheckDigit);
        }

        /// <summary>ICAO 9303 Part 11 §4.3.2's MRZ_information concatenation: document number
        /// padded to 9 characters with the filler character, immediately followed by each field's
        /// check digit, in this exact order.</summary>
        public override byte[] SeedInputBytes
        {
            get
            {
                var mrzInformation = string.Concat(
                    DocumentNumber.PadRight(9, '<'),
                    DocumentNumberCheckDigit,
                    DateOfBirth,
                    DateOfBirthCheckDigit,
                    DateOfExpiry,
                    DateOfExpiryCheckDigit);
                return Encoding.ASCII.GetBytes(mrzInformation);
            }
        }

        private static bool IsSixDigitDate(string value) =>
            value is { Length: 6 } && value.All(char.IsAsciiDigit);

        private static void RequireCheckDigitAlphabet(char checkDigit, string paramName)
        {
            if (!char.IsAsciiDigit(checkDigit) && checkDigit != '<')
            {
                throw new ArgumentException("check digit must be 0-9 or '<'", paramName);
            }
        }
    }

    /// <summary>PACE's CAN-based key-seed source: a 6-digit Card Access Number the user reads
    /// directly off the document and enters - "user interaction" in AC01's wording, requiring no
    /// MRZ recognition at all.</summary>
    public sealed record CardAccessNumber : NfcAccessKeySeedMaterial
    {
        public string Value { get; }

        private CardAccessNumber(string value) => Value = value;

        public static CardAccessNumber Create(string value)
        {
            if (value is not { Length: 6 } || !value.All(char.IsAsciiDigit))
            {
                throw new ArgumentException("card access number must be exactly 6 digits", nameof(value));
            }

            return new CardAccessNumber(value);
        }

        public override byte[] SeedInputBytes => Encoding.ASCII.GetBytes(Value);
    }
}
