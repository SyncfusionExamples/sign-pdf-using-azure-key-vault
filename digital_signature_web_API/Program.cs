using Azure.Identity;
using Azure.Security.KeyVault.Certificates;
using Azure.Security.KeyVault.Keys.Cryptography;
using Syncfusion.Pdf.Parsing;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowBlazorClient",
          builder =>
          {
              builder.WithOrigins("https://localhost:7277/")//Blazor app URL
              .SetIsOriginAllowed((host) => true)
                     .AllowAnyHeader()
                     .AllowAnyMethod()
              .AllowCredentials();
          });
});

var app = builder.Build();

app.MapGet("/", () => "Hello World!");


app.MapPost("api/signPDF", async (HttpContext context) =>
{
    var request = await context.Request.ReadFormAsync();

    if (request.Files.Count>0)
    {
        var pdfFile = request.Files[0].OpenReadStream();
        String tenantId = "tenantID";
        String clientId = "clientID";
        String secret = "secret";
        String vaultUri = "https://signature.vault.azure.net/";

        ClientSecretCredential credential = new ClientSecretCredential(tenantId, clientId, secret);

        //Get the public certificate to sign the PDF document
        X509Certificate2 pubCertifciate = GetPublicCertificate(credential, vaultUri);

        //Build the certificate chain.
        X509Chain chain = new X509Chain();
        chain.Build(pubCertifciate);

        List<X509Certificate2> certificates = new List<X509Certificate2>();
        for (int i = 0; i < chain.ChainElements.Count; i++)
        {
            certificates.Add(chain.ChainElements[i].Certificate);
        }

        PdfLoadedDocument loadedDocument = new PdfLoadedDocument(pdfFile);

        //Load the existing page.
        PdfLoadedPage? page = loadedDocument.Pages[0] as PdfLoadedPage;

        //Create a new PDF signature object.
        PdfSignature signature = new PdfSignature(loadedDocument, page!, null, "Sig1");

        signature.Bounds = new Syncfusion.Drawing.RectangleF(0, 0, 200, 100);

        //Create CryptographyClient with key identifier
        CryptographyClient client = new CryptographyClient(new Uri("https://signature.vault.azure.net/keys/PDFSigner/adb90908592644f69e0e61bcf7c69ff4"), credential);

        //Sing using external signer.
        signature.AddExternalSigner(new ExternalSigner(client), certificates, null);

        signature.Settings.DigestAlgorithm = DigestAlgorithm.SHA256;

        MemoryStream ms = new MemoryStream();

        //Save and close the document.
        loadedDocument.Save(ms);

        ms.Position = 0;

        loadedDocument.Close(true);

        context.Response.ContentType = "application/pdf";
        await context.Response.Body.WriteAsync(ms.ToArray());
    }



});

app.UseCors("AllowBlazorClient");

app.Run();


X509Certificate2 GetPublicCertificate(ClientSecretCredential credential, String uri)
{
    //Create certificate client.
    CertificateClient certificateClient = new CertificateClient(new Uri(uri), credential);

    //Get the certificate with public key.
    KeyVaultCertificateWithPolicy certificate = certificateClient.GetCertificateAsync("PDFSigner").Result;

    //Create and return the X509Certificate2.
    return new X509Certificate2(certificate.Cer);
}

//External signer to sign the PDF document using Azure Key Vault.
internal class ExternalSigner : IPdfExternalSigner
{
    public string HashAlgorithm => "SHA256";
    private CryptographyClient keyClient;

    public ExternalSigner(CryptographyClient client)
    {
        keyClient = client;
    }

    public byte[] Sign(byte[] message, out byte[] timeStampResponse)
    {
        var digest = SHA256.Create().ComputeHash(message);
        timeStampResponse = null;

        //Sign the hash of the PDF document
        return keyClient
            .SignAsync(
                SignatureAlgorithm.RS256,
                digest)
            .Result.Signature;
    }
}


