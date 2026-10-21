using System.Text.RegularExpressions;

namespace JobAutopilot.Core.Applications;

/// <summary>How a vacancy takes applications (step 08a). Only detected and shown — nothing is ever sent from here.</summary>
public enum ApplyChannel
{
    Unknown,
    Email,
    Portal,
    Form,
    External,
}

/// <summary>Where the channel came from: a structured field of the source, a rule on the text, or the local model.</summary>
public enum ApplySource
{
    None,
    Field,
    Rule,
    Ai,
}

/// <summary>
/// The detected application channel: an address (only one that occurs in the text), contact person, whether e-mail is
/// welcome or explicitly refused („Bewerbungen per E-Mail werden nicht berücksichtigt"), what the employer asks for.
/// </summary>
public sealed record HowToApply(
    ApplyChannel Channel,
    string? Email,
    string? ContactName,
    string? Salutation,
    bool EmailAllowed,
    bool PortalOnly,
    IReadOnlyList<string> Requested,
    ApplySource Source,
    string? ExternalUrl = null)
{
    public static readonly HowToApply Unknown = new(ApplyChannel.Unknown, null, null, null, false, false, [], ApplySource.None);

    /// <summary>„vollständig vorbereitbar": by e-mail, not portal-only, with a description to write the letter from.</summary>
    public bool EmailReady(bool hasDescription) => Channel == ApplyChannel.Email && Email is not null && !PortalOnly && hasDescription;
}

/// <summary>
/// Rules on the posting (08a): e-mail addresses including common obfuscations („name (at) firma.de", „[at]",
/// „ at … dot …"), application inboxes ranked before info@ (privacy / newsletter addresses dropped), the contact person,
/// „per E-Mail an …" vs. „ausschließlich über unser Karriereportal", and what is requested. The posting is untrusted
/// text: addresses are only shown, never contacted.
/// </summary>
public static partial class HowToApplyDetector
{
    /// <param name="url">The vacancy's link: a page outside the Arbeitsagentur (externeURL, Himalayas applicationLink) is a structured hint.</param>
    public static HowToApply Detect(string? description, string? url)
    {
        var text = description ?? string.Empty;
        var emails = Emails(text);
        var contact = Contact(text);
        var email = Best(emails, contact.Name);
        var portalOnly = PortalOnlyPhrase().IsMatch(text);
        var emailAllowed = !portalOnly && (EmailAllowedPhrase().IsMatch(text) || (email is not null && AddressedTo(text, email)));
        var requested = Requested(text);
        var external = IsExternal(url) ? url : null;

        var (channel, source) = portalOnly ? (ApplyChannel.Portal, ApplySource.Rule)
            : email is not null ? (ApplyChannel.Email, ApplySource.Rule)
            : FormPhrase().IsMatch(text) ? (ApplyChannel.Form, ApplySource.Rule)
            : PortalPhrase().IsMatch(text) ? (ApplyChannel.Portal, ApplySource.Rule)
            : external is not null ? (ApplyChannel.External, ApplySource.Field)
            : (ApplyChannel.Unknown, ApplySource.None);
        return new HowToApply(channel, email, contact.Name, contact.Salutation, emailAllowed, portalOnly, requested, source, external);
    }

    /// <summary>
    /// Rules first (an address found by the rules is checked against the text by construction); the model fills what
    /// the rules left open — its address was already checked against the text by the extraction guard.
    /// </summary>
    public static HowToApply Resolve(string? description, string? url, Extraction.ApplicationExtraction? ai)
    {
        var rules = Detect(description, url);
        if (ai is null || (rules.Channel is not (ApplyChannel.Unknown or ApplyChannel.External) && (rules.Email is not null || ai.Email is null)))
        {
            return rules with { ContactName = rules.ContactName ?? ai?.ContactName, Salutation = rules.Salutation ?? ai?.Salutation };
        }

        var channel = ai.Email is not null && !rules.PortalOnly && !ai.PortalOnly ? ApplyChannel.Email
            : ai.Channel is ApplyChannel.Unknown ? rules.Channel
            : ai.Channel == ApplyChannel.Email && ai.Email is null ? rules.Channel
            : ai.Channel;
        return new HowToApply(
            channel,
            channel == ApplyChannel.Email ? rules.Email ?? ai.Email : rules.Email,
            rules.ContactName ?? ai.ContactName,
            rules.Salutation ?? ai.Salutation,
            rules.EmailAllowed || ai.EmailAllowed,
            rules.PortalOnly || ai.PortalOnly,
            [.. rules.Requested.Union(ai.Requested, StringComparer.OrdinalIgnoreCase)],
            channel == rules.Channel ? rules.Source : ApplySource.Ai,
            rules.ExternalUrl);
    }

    /// <summary>All addresses in the text after de-obfuscation, lower case, without duplicates.</summary>
    public static IReadOnlyList<string> Emails(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var found = new List<string>();
        foreach (Match match in ObfuscatedEmail().Matches(text))
        {
            var local = match.Groups["local"].Value.Trim('.', '-');
            var domain = DomainSeparator().Replace(match.Groups["domain"].Value, ".").Trim('.');
            var tld = domain[(domain.LastIndexOf('.') + 1)..];
            if (local.Length == 0 || !domain.Contains('.', StringComparison.Ordinal) || tld.Length is < 2 or > 24 || !tld.All(char.IsLetter))
            {
                continue;
            }

            var address = $"{local}@{domain}".ToLowerInvariant();
            if (!found.Contains(address))
            {
                found.Add(address);
            }
        }

        return found;
    }

    /// <summary>bewerbung@ / jobs@ / karriere@ … first, then a named person, then the rest; info@ last; privacy, newsletter, no-reply never.</summary>
    public static string? Best(IReadOnlyList<string> emails, string? contactName = null)
    {
        ArgumentNullException.ThrowIfNull(emails);
        return emails
            .Select((e, i) => (Email: e, Order: i, Score: Score(e, contactName)))
            .Where(e => e.Score > 0)
            .OrderByDescending(e => e.Score)
            .ThenBy(e => e.Order)
            .Select(e => e.Email)
            .FirstOrDefault();
    }

    private static int Score(string email, string? contactName)
    {
        var local = email[..email.IndexOf('@', StringComparison.Ordinal)];
        if (IgnoredInbox().IsMatch(local))
        {
            return 0;
        }

        if (ApplicationInbox().IsMatch(local))
        {
            return 4;
        }

        if (contactName is not null && contactName.Split(' ').Any(n => n.Length > 2 && local.Contains(n, StringComparison.OrdinalIgnoreCase)))
        {
            return 3;
        }

        return GenericInbox().IsMatch(local) ? 1 : 2;
    }

    /// <summary>„Ansprechpartnerin: Frau Lisa Müller", „Deine Ansprechpartnerin Lisa freut sich …", „Fragen? … Herr Maier".</summary>
    public static (string? Name, string? Salutation) Contact(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var match = ContactPattern().Match(text);
        if (!match.Success)
        {
            return (null, null);
        }

        var title = match.Groups["title"].Value.Trim();
        var name = match.Groups["name"].Value.Trim();
        var salutation = title switch
        {
            "Frau" => $"Sehr geehrte Frau {name.Split(' ')[^1]},",
            "Herr" => $"Sehr geehrter Herr {name.Split(' ')[^1]},",
            _ => null,
        };
        return (title.Length > 0 ? $"{title} {name}" : name, salutation);
    }

    public static IReadOnlyList<string> Requested(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var requested = new List<string>();
        if (SalaryAsked().IsMatch(text))
        {
            requested.Add("Gehaltsvorstellung");
        }

        if (StartAsked().IsMatch(text))
        {
            requested.Add("Eintrittstermin");
        }

        if (Certificates().IsMatch(text))
        {
            requested.Add("Zeugnisse");
        }

        if (NoCoverLetter().IsMatch(text))
        {
            requested.Add("ohne Anschreiben");
        }

        return requested;
    }

    private static bool AddressedTo(string text, string email)
    {
        var at = text.IndexOf(email.Split('@')[0], StringComparison.OrdinalIgnoreCase);
        return at > 0 && SendTo().IsMatch(text[Math.Max(0, at - 80)..at]);
    }

    private static bool IsExternal(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && !uri.Host.EndsWith("arbeitsagentur.de", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"(?<local>[A-Za-z0-9][A-Za-z0-9._%+-]*)\s*(?:@|\(\s*(?:at|ät|@)\s*\)|\[\s*(?:at|ät|@)\s*\]|\{\s*(?:at|@)\s*\}|\s(?:at|AT)\s)\s*(?<domain>[A-Za-z0-9-]+(?:(?:\.(?=[A-Za-z0-9])|\s*\(\s*(?:dot|punkt)\s*\)\s*|\s*\[\s*(?:dot|punkt)\s*\]\s*|\s(?:dot|punkt)\s)[A-Za-z0-9-]+)+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 500)]
    private static partial Regex ObfuscatedEmail();

    [GeneratedRegex(@"\.|\s*\(\s*(?:dot|punkt)\s*\)\s*|\s*\[\s*(?:dot|punkt)\s*\]\s*|\s(?:dot|punkt)\s", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex DomainSeparator();

    [GeneratedRegex(@"^(?:bewerbung(?:en)?|bewerber\w*|jobs?|karriere|career|careers|hr|recruiting|recruitment|personal|personalabteilung|talent\w*|apply|application|stellen|people)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex ApplicationInbox();

    [GeneratedRegex(@"^(?:info|kontakt|contact|office|mail|hello|hallo|service|zentrale)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex GenericInbox();

    [GeneratedRegex(@"^(?:datenschutz|privacy|dsb|dpo|newsletter|no-?reply|noreply|presse|press|support|abuse|webmaster|rechnung|buchhaltung|invoice)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex IgnoredInbox();

    [GeneratedRegex(@"(?:Ansprechpartner(?:in)?|Ansprechperson|Kontaktperson|Fragen\?[^.\n]{0,60}?(?=Frau|Herr))\s*(?:ist|sind|für dich|für Sie)?\s*:?\s*(?:(?<title>Frau|Herr)\s+)?(?<name>\p{Lu}\p{Ll}+(?:\s+\p{Lu}\p{Ll}+){0,2})", RegexOptions.CultureInvariant, 250)]
    private static partial Regex ContactPattern();

    [GeneratedRegex(@"\b(?:ausschließlich|nur)\s+(?:online|über\s+(?:unser|unsere|das|die|den)\s+[\w‑-]*(?:portal|system|formular|homepage|karriereseite|website|webseite|plattform)\w*)|Bewerbungen\s+(?:per|via)\s+(?:E-?Mail|Mail)\s+(?:werden|können)\s+(?:wir\s+)?(?:leider\s+)?nicht|keine\s+Bewerbungen\s+(?:per|via)\s+(?:E-?Mail|Mail)|only\s+(?:via|through)\s+our\s+[\w-]*\s*(?:portal|system|website)|(?:do|will)\s+not\s+accept\s+applications\s+(?:by|via)\s+e-?mail", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex PortalOnlyPhrase();

    [GeneratedRegex(@"\b(?:per|via|by)\s+(?:E-?Mail|Mail)\b|ganz\s+klassisch\s+per|an\s+folgende\s+(?:E-?Mail|Adresse)|Unterlagen\s+(?:bitte\s+)?an\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex EmailAllowedPhrase();

    [GeneratedRegex(@"(?:schick|send|sende|richte|richten|bewirb|bewerben|Unterlagen|Bewerbung)\w*[^.\n]{0,60}\ban\b[^.\n]{0,20}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex SendTo();

    [GeneratedRegex(@"\b(?:Online-?Formular|Bewerbungsformular|Kontaktformular|über\s+(?:das|unser)\s+Formular|application\s+form)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex FormPhrase();

    [GeneratedRegex(@"\b(?:Karriereportal|Bewerberportal|Jobportal|Bewerbungsportal|Online-?Bewerbung\w*|Bewerbermanagement\w*|Karriereseite|online\s+bewerben|Jetzt\s+bewerben|apply\s+now|career\s+(?:portal|page))\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex PortalPhrase();

    [GeneratedRegex(@"\bGehaltsvorstellung\w*|\bGehaltswunsch|\bsalary\s+expectation", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex SalaryAsked();

    [GeneratedRegex(@"\b(?:frühestmöglichen?\s+)?Eintrittstermin\w*|\bVerfügbarkeit\b|\bStarttermin|\bearliest\s+start", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex StartAsked();

    [GeneratedRegex(@"\b(?:Arbeits)?[Zz]eugnisse\b|\bZeugniskopien\b|\breferences\b", RegexOptions.CultureInvariant, 250)]
    private static partial Regex Certificates();

    [GeneratedRegex(@"\bohne\s+Anschreiben\b|\bAnschreiben\s+(?:ist\s+)?(?:optional|nicht\s+(?:nötig|notwendig|erforderlich))|\bkein\s+Anschreiben\b|\bno\s+cover\s+letter", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex NoCoverLetter();
}
