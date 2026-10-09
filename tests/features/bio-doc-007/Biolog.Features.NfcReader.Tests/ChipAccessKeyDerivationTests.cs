using Biolog.Features.NfcReader.Model;
using Xunit;

namespace Biolog.Features.NfcReader.Tests;

/// <summary>Developer tests for BIO-DOC-007-AC01 ("NFC access keys are derived only from permitted
/// document data/user interaction").</summary>
public sealed class ChipAccessKeyDerivationTests
{
    /// <summary>
    /// Known-answer test: the expected bytes below were computed independently with Python's
    /// <c>hashlib</c> (a wholly separate SHA-1 implementation from .NET's), running the exact
    /// algorithm this class documents (SHA-1 seed/KDF, 4-byte big-endian counter, DES odd-parity
    /// adjustment) over ICAO 9303's own "ERIKSSON ANNA MARIA" TD3 worked example fields
    /// (document number L898902C3, check digit 6, DOB 740812, check digit 2, expiry 120415, check
    /// digit 9 - the same fields <c>MrzSampleData.Td3Line2</c> in BIO-DOC-002's tests uses). This
    /// proves this implementation's SHA-1 usage, counter encoding and parity adjustment agree with
    /// an independent implementation of the same documented algorithm - see
    /// <see cref="ChipAccessKeyDerivation"/>'s remarks for why this is deliberately NOT claimed to
    /// reproduce a memorized ICAO-published numeric constant.
    /// </summary>
    [Fact]
    public void AC01_key_derivation_matches_an_independent_python_hashlib_implementation_of_the_same_algorithm()
    {
        var seedMaterial = NfcAccessKeySeedMaterial.Mrz.Create(
            documentNumber: "L898902C3",
            documentNumberCheckDigit: '6',
            dateOfBirth: "740812",
            dateOfBirthCheckDigit: '2',
            dateOfExpiry: "120415",
            dateOfExpiryCheckDigit: '9');

        var seed = ChipAccessKeyDerivation.DeriveKeySeed(seedMaterial);
        Assert.Equal("3f181d701dd9f12e525ef9b5ebef8909", Convert.ToHexStringLower(seed));

        var keys = ChipAccessKeyDerivation.DeriveKeys(seedMaterial);
        Assert.Equal("3d6ea789f8973d023b435b104fa8d56b", Convert.ToHexStringLower(keys.EncryptionKey));
        Assert.Equal("dfd63e011a57f44c16a43b236eab456b", Convert.ToHexStringLower(keys.MacKey));
    }

    [Fact]
    public void AC01_derivation_is_deterministic()
    {
        var seedMaterial = SampleMrz();

        var keys1 = ChipAccessKeyDerivation.DeriveKeys(seedMaterial);
        var keys2 = ChipAccessKeyDerivation.DeriveKeys(seedMaterial);

        Assert.Equal(keys1.EncryptionKey, keys2.EncryptionKey);
        Assert.Equal(keys1.MacKey, keys2.MacKey);
    }

    [Fact]
    public void AC01_the_encryption_and_mac_keys_are_different()
    {
        var keys = ChipAccessKeyDerivation.DeriveKeys(SampleMrz());

        Assert.NotEqual(keys.EncryptionKey, keys.MacKey);
    }

    [Fact]
    public void AC01_different_document_data_produces_different_keys()
    {
        var keysA = ChipAccessKeyDerivation.DeriveKeys(SampleMrz());
        var keysB = ChipAccessKeyDerivation.DeriveKeys(NfcAccessKeySeedMaterial.Mrz.Create(
            documentNumber: "D23145890",
            documentNumberCheckDigit: '4',
            dateOfBirth: "740812",
            dateOfBirthCheckDigit: '2',
            dateOfExpiry: "120415",
            dateOfExpiryCheckDigit: '9'));

        Assert.NotEqual(keysA.EncryptionKey, keysB.EncryptionKey);
    }

    [Fact]
    public void AC01_a_card_access_number_derives_a_different_key_than_the_mrz_for_the_same_document()
    {
        var mrzKeys = ChipAccessKeyDerivation.DeriveKeys(SampleMrz());
        var canKeys = ChipAccessKeyDerivation.DeriveKeys(NfcAccessKeySeedMaterial.CardAccessNumber.Create("123456"));

        Assert.NotEqual(mrzKeys.EncryptionKey, canKeys.EncryptionKey);
    }

    [Fact]
    public void AC01_QA02_mutating_a_returned_key_array_never_corrupts_the_stored_key_for_later_callers()
    {
        var keys = ChipAccessKeyDerivation.DeriveKeys(SampleMrz());
        var original = (byte[])keys.EncryptionKey.Clone();

        var handle = keys.EncryptionKey;
        for (var i = 0; i < handle.Length; i++)
        {
            handle[i] ^= 0xFF; // an attacker/buggy caller corrupting the array they were handed
        }

        // A fresh access still returns the real, untouched key - the mutation above never reached
        // the value NfcAccessKeys actually holds (BIO-DOC-007-QA-02).
        Assert.Equal(original, keys.EncryptionKey);
    }

    [Fact]
    public void AC01_QA02_two_accesses_to_the_same_key_never_return_the_same_array_instance()
    {
        // Each getter access returns a fresh defensive copy - a caller can never hold a live
        // reference to the array NfcAccessKeys actually stores internally (BIO-DOC-007-QA-02).
        var keys = ChipAccessKeyDerivation.DeriveKeys(SampleMrz());

        Assert.NotSame(keys.EncryptionKey, keys.EncryptionKey);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void AC01_every_derived_key_byte_has_odd_parity(int counter)
    {
        var seed = ChipAccessKeyDerivation.DeriveKeySeed(SampleMrz());
        var key = ChipAccessKeyDerivation.DeriveKey(seed, counter);

        foreach (var b in key)
        {
            var onesInByte = System.Numerics.BitOperations.PopCount(b);
            Assert.True(onesInByte % 2 == 1, $"byte 0x{b:x2} does not have odd parity");
        }
    }

    [Fact]
    public void AC01_seed_material_carries_only_the_permitted_mrz_fields_nothing_else()
    {
        var propertyNames = typeof(NfcAccessKeySeedMaterial.Mrz)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet();

        Assert.Equal(
            new HashSet<string>
            {
                "DocumentNumber", "DocumentNumberCheckDigit",
                "DateOfBirth", "DateOfBirthCheckDigit",
                "DateOfExpiry", "DateOfExpiryCheckDigit",
                "SeedInputBytes",
            },
            propertyNames);
    }

    [Fact]
    public void AC01_a_malformed_date_of_birth_is_rejected_not_silently_truncated()
    {
        Assert.Throws<ArgumentException>(() => NfcAccessKeySeedMaterial.Mrz.Create(
            documentNumber: "L898902C3", documentNumberCheckDigit: '6',
            dateOfBirth: "74081", dateOfBirthCheckDigit: '2', // 5 digits, not 6
            dateOfExpiry: "120415", dateOfExpiryCheckDigit: '9'));
    }

    [Fact]
    public void AC01_a_card_access_number_must_be_exactly_six_digits()
    {
        Assert.Throws<ArgumentException>(() => NfcAccessKeySeedMaterial.CardAccessNumber.Create("12345"));
        Assert.Throws<ArgumentException>(() => NfcAccessKeySeedMaterial.CardAccessNumber.Create("1234567"));
        Assert.Throws<ArgumentException>(() => NfcAccessKeySeedMaterial.CardAccessNumber.Create("12345a"));
    }

    private static NfcAccessKeySeedMaterial.Mrz SampleMrz() => NfcAccessKeySeedMaterial.Mrz.Create(
        documentNumber: "L898902C3",
        documentNumberCheckDigit: '6',
        dateOfBirth: "740812",
        dateOfBirthCheckDigit: '2',
        dateOfExpiry: "120415",
        dateOfExpiryCheckDigit: '9');
}
