namespace Vora.Application.Auth.External;

public interface IAuthSecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string ciphertext);
}
