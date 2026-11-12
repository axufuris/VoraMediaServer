namespace Vora.Application.Iptv;

public interface IIptvSecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string ciphertext);
}
