using System.Security.Cryptography;
using iText.Bouncycastleconnector;
using iText.Commons.Digest;
using iText.Kernel.Pdf;
using iText.Signatures;
using MasterSTI.Api.Common;
using MasterSTI.Api.Common.Signing;
using Microsoft.Extensions.Logging.Abstractions;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Tsp;
using Org.BouncyCastle.X509;

namespace MasterSTI.UnitTests;

/// <summary>
/// Issue-#62 binary proof of PAdES B-LTA archive-timestamp emission. Produces a
/// CMS-signed PDF via <see cref="PadesService"/>, stamps an archive timestamp on
/// top via <see cref="PdfSigner.Timestamp"/> driven by an in-process RFC 3161 TSA
/// (no network), then re-opens the result and asserts the structural invariants
/// the dissertation's B-LTA claim depends on:
///   * a second signature exists on top of the original CMS signature
///   * its <c>/SubFilter</c> is <c>/ETSI.RFC3161</c> (the archive-timestamp wire format)
///   * its <c>/Type</c> is <c>/DocTimeStamp</c> (the document-level-timestamp dictionary type)
///   * the embedded token parses as a valid RFC 3161 <c>TimeStampToken</c> with a
///     parseable <c>genTime</c> close to the test's wall clock.
/// </summary>
public class PadesLtaArchiveTimestampTests
{
    private static PadesService CreateService() => new(NullLogger<PadesService>.Instance);

    private static byte[] CreateMinimalPdf()
    {
        using var ms = new MemoryStream();
        using var writer = new PdfWriter(ms);
        using var doc = new PdfDocument(writer);
        doc.AddNewPage();
        doc.Close();
        return ms.ToArray();
    }

    private static byte[] StubCmsBytes()
    {
        // Same shape as EmbedSignatureHandlerTests: structurally well-formed
        // ASN.1 SEQUENCE of 508 bytes. iText writes it verbatim into /Contents.
        var bytes = new byte[512];
        bytes[0] = 0x30;
        bytes[1] = 0x82;
        bytes[2] = 0x01;
        bytes[3] = 0xFC;
        return bytes;
    }

    [Fact]
    public void ArchiveTimestamp_EmitsDocTimeStampWithEtsiRfc3161SubFilter()
    {
        // ----- 1. Build a CMS-signed PDF (the B-T/B-LT input). ----------------------------
        var pades = CreateService();
        var prepared = pades.Prepare(CreateMinimalPdf());
        var bTPdf = pades.Embed(prepared.PreparedPdfBytes, StubCmsBytes(), prepared.SignatureFieldName);

        // ----- 2. Apply archive timestamp via PdfSigner + in-process TSA. -----------------
        // Mirrors LtvService.AddArchiveTimestampAsync: PdfSigner.Timestamp(ITSAClient, name)
        // in append-mode, which is the exact iText API that emits the B-LTA structure.
        var lTaPdf = AppendArchiveTimestamp(bTPdf, fieldName: "ArchiveTimestamp_T1");

        // ----- 3. Assert structural invariants on the resulting PDF. ----------------------
        using var reader = new PdfReader(new MemoryStream(lTaPdf));
        using var pdfDoc = new PdfDocument(reader);
        var sigUtil = new SignatureUtil(pdfDoc);
        var sigNames = sigUtil.GetSignatureNames();

        Assert.Equal(2, sigNames.Count); // original signature + archive timestamp

        var dts = sigUtil.GetSignature("ArchiveTimestamp_T1");
        Assert.NotNull(dts);
        Assert.Equal("/ETSI.RFC3161", dts!.GetSubFilter().ToString());
        // /Type=/DocTimeStamp is what tells PDF readers this is a document-level
        // timestamp signature (PAdES B-LTA) rather than a content signature.
        var sigDict = ReadSignatureDictionary(lTaPdf, "ArchiveTimestamp_T1");
        Assert.Equal("/DocTimeStamp", sigDict.GetAsName(PdfName.Type)?.ToString());

        // ----- 4. /Contents bytes parse as a real RFC 3161 TimeStampToken. ----------------
        // Read /Contents directly from the signature dictionary — iText's
        // ReadSignatureData is designed for CMS signatures and re-encodes via
        // PdfPKCS7, which trips on a pure DocTimeStamp.
        var contentsString = sigDict.GetAsString(PdfName.Contents)
            ?? throw new InvalidOperationException("DocTimeStamp has no /Contents");
        var tokenBytes = TrimPadding(contentsString.GetValueBytes());
        Assert.NotEmpty(tokenBytes);
        var genTime = RfcTimestampDecoder.TryDecodeGenTime(Convert.ToBase64String(tokenBytes));
        Assert.NotNull(genTime);
        Assert.True(Math.Abs((genTime!.Value - DateTime.UtcNow).TotalMinutes) < 1,
            $"TSA genTime should be close to now; was {genTime.Value:o}");
    }

    [Fact]
    public void ArchiveTimestamp_OriginalContentSignatureSurvivesUntouched()
    {
        // The B-LTA upgrade must be append-only: the original /Contents bytes of
        // the content signature must be byte-for-byte identical after the archive
        // timestamp is applied. This is the cryptographic invariant that lets a
        // long-term verifier walk back from the DocTimeStamp to the original
        // CMS without the signature breaking.
        var pades = CreateService();
        var prepared = pades.Prepare(CreateMinimalPdf());
        var bTPdf = pades.Embed(prepared.PreparedPdfBytes, StubCmsBytes(), prepared.SignatureFieldName);

        var originalContents = ReadContentsHex(bTPdf, prepared.SignatureFieldName);

        var lTaPdf = AppendArchiveTimestamp(bTPdf, fieldName: "ArchiveTimestamp_T2");

        var contentsAfterDts = ReadContentsHex(lTaPdf, prepared.SignatureFieldName);
        Assert.Equal(originalContents, contentsAfterDts);
    }

    // ---- Helpers ---------------------------------------------------------------------------

    /// <summary>
    /// Equivalent of LtvService.AddArchiveTimestampAsync but with an in-process
    /// ITSAClient instead of TSAClientBouncyCastle, so the test never touches the
    /// network.
    /// </summary>
    private static byte[] AppendArchiveTimestamp(byte[] bTPdf, string fieldName)
    {
        using var inputMs = new MemoryStream(bTPdf);
        using var outputMs = new MemoryStream();
        using var reader = new PdfReader(inputMs);

        var signer = new PdfSigner(reader, outputMs, new StampingProperties().UseAppendMode());
        signer.Timestamp(new InProcessTsaClient(), fieldName);
        return outputMs.ToArray();
    }

    private static PdfDictionary ReadSignatureDictionary(byte[] pdfBytes, string fieldName)
    {
        using var reader = new PdfReader(new MemoryStream(pdfBytes));
        using var pdf = new PdfDocument(reader);
        var acroForm = iText.Forms.PdfAcroForm.GetAcroForm(pdf, false)!;
        var field = acroForm.GetField(fieldName)!.GetPdfObject();
        return field.GetAsDictionary(PdfName.V) ?? field;
    }

    /// <summary>
    /// iText pads the /Contents hex placeholder to a fixed size estimate and
    /// fills the unused tail with 0x00. The real RFC 3161 token is a DER
    /// SEQUENCE prefix, so trim trailing zeros that aren't part of the ASN.1.
    /// </summary>
    private static byte[] TrimPadding(byte[] padded)
    {
        var end = padded.Length;
        while (end > 0 && padded[end - 1] == 0)
            end--;
        var trimmed = new byte[end];
        Array.Copy(padded, trimmed, end);
        return trimmed;
    }

    private static string ReadContentsHex(byte[] pdfBytes, string fieldName)
    {
        var sigDict = ReadSignatureDictionary(pdfBytes, fieldName);
        var contents = sigDict.GetAsString(PdfName.Contents)
            ?? throw new InvalidOperationException("Signature has no /Contents");
        return Convert.ToHexString(contents.GetValueBytes());
    }

    /// <summary>
    /// In-process RFC 3161 TSA. Implements iText's <see cref="ITSAClient"/> by
    /// generating a fresh self-signed TSA cert + RSA keypair per instance and
    /// signing real <c>TimeStampToken</c>s via BouncyCastle's
    /// <see cref="TimeStampTokenGenerator"/>. No HTTP, no shared state.
    /// </summary>
    private sealed class InProcessTsaClient : ITSAClient
    {
        // Estimate generous enough to cover SHA-256 imprint + 2048-bit RSA signature
        // + cert + standard ASN.1 overhead. iText rounds the signature placeholder
        // up so an over-estimate is harmless; an under-estimate would crash.
        private const int TokenSizeEstimate = 8192;

        private readonly TimeStampTokenGenerator _tokenGen;
        private readonly SecureRandom _random = new();

        public InProcessTsaClient()
        {
            var keyGen = new RsaKeyPairGenerator();
            keyGen.Init(new Org.BouncyCastle.Crypto.KeyGenerationParameters(_random, 2048));
            var pair = keyGen.GenerateKeyPair();

            var certGen = new X509V3CertificateGenerator();
            certGen.SetSerialNumber(BigInteger.ValueOf(1));
            certGen.SetIssuerDN(new X509Name("CN=In-Process Test TSA"));
            certGen.SetSubjectDN(new X509Name("CN=In-Process Test TSA"));
            certGen.SetNotBefore(DateTime.UtcNow.AddDays(-1));
            certGen.SetNotAfter(DateTime.UtcNow.AddYears(1));
            certGen.SetPublicKey(pair.Public);
            certGen.AddExtension(X509Extensions.ExtendedKeyUsage, true,
                new ExtendedKeyUsage(KeyPurposeID.id_kp_timeStamping));
            var sigFactory = new Asn1SignatureFactory("SHA256WITHRSA", pair.Private, _random);
            var cert = certGen.Generate(sigFactory);

            _tokenGen = new TimeStampTokenGenerator(
                (RsaPrivateCrtKeyParameters)pair.Private,
                cert,
                TspAlgorithms.Sha256,
                "1.2.3.4.5");
            _tokenGen.SetCertificates(
                Org.BouncyCastle.Utilities.Collections.CollectionUtilities.CreateStore(new[] { cert }));
        }

        public int GetTokenSizeEstimate() => TokenSizeEstimate;

        public IMessageDigest GetMessageDigest() =>
            BouncyCastleFactoryCreator.GetFactory().CreateIDigest("SHA-256");

        public byte[] GetTimeStampToken(byte[] imprint)
        {
            var reqGen = new TimeStampRequestGenerator();
            // iText's SignatureUtil.ReadSignatureData walks the embedded cert
            // chain to recreate the signing cert, so the TSA must include it.
            reqGen.SetCertReq(true);
            var req = reqGen.Generate(TspAlgorithms.Sha256, imprint, BigInteger.ValueOf(_random.NextLong()));

            var respGen = new TimeStampResponseGenerator(_tokenGen, TspAlgorithms.Allowed);
            var resp = respGen.Generate(req, BigInteger.ValueOf(_random.NextLong()), DateTime.UtcNow);
            return resp.TimeStampToken.GetEncoded();
        }
    }
}
