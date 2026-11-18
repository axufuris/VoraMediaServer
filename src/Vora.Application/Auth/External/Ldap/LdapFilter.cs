using System.Text;

namespace Vora.Application.Auth.External.Ldap;

public static class LdapFilter
{
    public const int MaxUsernameLength = 256;
    public const int MaxTemplateLength = 1024;
    public const string UsernamePlaceholder = "{username}";
    public const string DnPlaceholder = "{dn}";

    public static string Escape(string value)
    {
        var builder = new StringBuilder(value.Length + 8);
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\':
                    builder.Append("\\5c");
                    break;
                case '*':
                    builder.Append("\\2a");
                    break;
                case '(':
                    builder.Append("\\28");
                    break;
                case ')':
                    builder.Append("\\29");
                    break;
                case '\0':
                    builder.Append("\\00");
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }

        return builder.ToString();
    }

    public static bool IsValidUsername(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= MaxUsernameLength && !value.Any(char.IsControl);

    public static string BuildUserFilter(string template, string username) =>
        template.Replace(UsernamePlaceholder, Escape(username.Trim()), StringComparison.Ordinal);

    public static string BuildGroupFilter(string template, string userDn, string username) =>
        template
            .Replace(DnPlaceholder, Escape(userDn), StringComparison.Ordinal)
            .Replace(UsernamePlaceholder, Escape(username.Trim()), StringComparison.Ordinal);

    public static string BuildProbeFilter(string template) =>
        template.Replace(UsernamePlaceholder, "*", StringComparison.Ordinal);

    public static string? ValidateTemplate(string? template, params string[] anyOfPlaceholders)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return "Enter a filter.";
        }

        if (template.Length > MaxTemplateLength || template.Any(char.IsControl))
        {
            return "The filter is too long or holds a control character.";
        }

        if (template[0] != '(' || template[^1] != ')')
        {
            return "A filter starts with ( and ends with ), such as (&(objectClass=person)(uid={username})).";
        }

        var depth = 0;
        for (var i = 0; i < template.Length; i++)
        {
            if (template[i] == '\\' && i + 1 < template.Length)
            {
                i++;
                continue;
            }

            if (template[i] == '(')
            {
                depth++;
            }
            else if (template[i] == ')')
            {
                depth--;
                if (depth < 0)
                {
                    return "The filter has a ) without a matching (.";
                }
            }
        }

        if (depth != 0)
        {
            return "The filter has a ( without a matching ).";
        }

        return anyOfPlaceholders.Any(placeholder => template.Contains(placeholder, StringComparison.Ordinal))
            ? null
            : $"The filter must contain {string.Join(" or ", anyOfPlaceholders)}, where the value to look for goes.";
    }
}
