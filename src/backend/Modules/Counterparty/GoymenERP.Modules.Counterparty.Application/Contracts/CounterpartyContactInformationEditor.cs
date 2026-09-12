using System.Globalization;
using System.Net.Mail;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Counterparty.Application.Contracts;

/// <summary>Deterministic, account-owned contact updates; never a global Party mutation.</summary>
public static class CounterpartyContactInformationEditor
{
    public const int MaximumOperationsPerCollection = 100;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static CounterpartyContactInformation Empty() => new(null, null, null, null, [], []);

    public static long ParseVersion(string? value)
    {
        if (string.IsNullOrEmpty(value) || value[0] == '0'
            || value.Any(character => character is < '0' or > '9')
            || !long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            || parsed < 1)
        {
            throw Invalid("expectedVersion", "Sürüm 1 ile Int64 üst sınırı arasında kanonik ondalık metin olmalıdır.");
        }

        return parsed;
    }

    public static CounterpartyContactInformation Load(string? json)
    {
        if (json is null)
        {
            return Empty();
        }

        try
        {
            var stored = JsonSerializer.Deserialize<StoredInformation>(json, JsonOptions);
            if (stored is null || stored.SchemaVersion != 1 || stored.Information is null)
            {
                throw new InvalidOperationException("Unknown contact information storage schema.");
            }

            ValidateStored(stored.Information);
            return stored.Information;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or NullReferenceException)
        {
            throw new InvalidOperationException("Stored contact information is invalid.", exception);
        }
    }

    public static string Serialize(CounterpartyContactInformation information) =>
        JsonSerializer.Serialize(new StoredInformation(1, information), JsonOptions);

    public static CounterpartyContactInformation Apply(
        CounterpartyContactInformation current, CounterpartyContactInformationChanges changes)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(changes);
        CheckOperations(changes.Addresses, change => change.Id, "changes.addresses");
        CheckOperations(changes.Contacts, change => change.Id, "changes.contacts");
        var addresses = current.Addresses.ToDictionary(address => address.Id);
        foreach (var change in changes.Addresses)
        {
            addresses.TryGetValue(change.Id, out var old);
            var active = ActiveState(change.Operation, old?.IsActive, "changes.addresses.operation");
            if (change.Operation != "upsert")
            {
                RequireNoFields("changes.addresses", change.Purpose, change.CountryCode, change.Line1,
                    change.Line2, change.City, change.District, change.PostalCode);
                addresses[change.Id] = old!.IsActive == active
                    ? old
                    : old with { IsActive = active, Version = Next(old.Version) };
                continue;
            }

            var purpose = Required(change.Purpose, old?.Purpose, 120, "changes.addresses.purpose");
            if (purpose is not ("billing" or "delivery" or "operational"))
            {
                throw Invalid("changes.addresses.purpose", "Yalnız billing, delivery veya operational kullanım amacı kabul edilir.");
            }

            var country = Required(change.CountryCode, old?.CountryCode, 2, "changes.addresses.countryCode")
                .ToUpperInvariant();
            CheckCountry(country);
            var address = new CounterpartyOperationalAddress(
                change.Id, old?.Version ?? "1", active, purpose, country,
                Required(change.Line1, old?.Line1, 250, "changes.addresses.line1"),
                Text(change.Line2, old?.Line2, 250, "changes.addresses.line2"),
                Text(change.City, old?.City, 100, "changes.addresses.city"),
                Text(change.District, old?.District, 100, "changes.addresses.district"),
                Text(change.PostalCode, old?.PostalCode, 20, "changes.addresses.postalCode"));
            addresses[change.Id] = old is not null && address != old
                ? address with { Version = Next(old.Version) } : address;
        }

        var contacts = current.Contacts.ToDictionary(contact => contact.Id);
        foreach (var change in changes.Contacts)
        {
            contacts.TryGetValue(change.Id, out var old);
            var active = ActiveState(change.Operation, old?.IsActive, "changes.contacts.operation");
            CheckOperations(change.Channels, channel => channel.Id, "changes.contacts.channels");
            if (change.Operation != "upsert")
            {
                RequireNoFields("changes.contacts", change.DisplayName, change.JobTitle, change.Purpose);
                if (change.Channels.Count != 0)
                {
                    throw Invalid("changes.contacts.channels", "Durum değişikliği kanal güncellemesi içeremez.");
                }

                contacts[change.Id] = old!.IsActive == active
                    ? old
                    : old with { IsActive = active, Version = Next(old.Version) };
                continue;
            }

            var channels = (old?.Channels ?? []).ToDictionary(channel => channel.Id);
            foreach (var channel in change.Channels)
            {
                channels.TryGetValue(channel.Id, out var previous);
                var channelActive = ActiveState(
                    channel.Operation, previous?.IsActive, "changes.contacts.channels.operation");
                if (channel.Operation != "upsert")
                {
                    RequireNoFields("changes.contacts.channels", channel.Kind, channel.Value, channel.Extension);
                    channels[channel.Id] = previous!.IsActive == channelActive
                        ? previous
                        : previous with { IsActive = channelActive, Version = Next(previous.Version) };
                    continue;
                }

                var kind = Required(channel.Kind, previous?.Kind, 10, "changes.contacts.channels.kind");
                if (kind is not ("phone" or "email") || previous is not null && kind != previous.Kind)
                {
                    throw Invalid("changes.contacts.channels.kind", "Kanal türü phone veya email olmalı; mevcut kanalın türü değiştirilemez.");
                }

                var value = Required(channel.Value, previous?.Value,
                    kind == "phone" ? 32 : 254, "changes.contacts.channels.value");
                if (kind == "email")
                {
                    CheckEmail(value, "changes.contacts.channels.value");
                }

                var extension = Text(channel.Extension, previous?.Extension, 10, "changes.contacts.channels.extension");
                if (extension is not null && kind != "phone")
                {
                    throw Invalid("changes.contacts.channels.extension", "Dahili yalnız telefon kanalında kullanılabilir.");
                }

                var updatedChannel = new CounterpartyContactChannel(channel.Id, previous?.Version ?? "1",
                    channelActive, kind, value, extension);
                channels[channel.Id] = previous is not null && updatedChannel != previous
                    ? updatedChannel with { Version = Next(previous.Version) } : updatedChannel;
            }

            var updatedContact = new CounterpartyOperationalContact(
                change.Id, old?.Version ?? "1", active,
                Required(change.DisplayName, old?.DisplayName, 200, "changes.contacts.displayName"),
                Text(change.JobTitle, old?.JobTitle, 120, "changes.contacts.jobTitle"),
                Required(change.Purpose, old?.Purpose, 120, "changes.contacts.purpose"),
                channels.Values.OrderBy(channel => channel.Id).ToArray());
            var unchanged = old is not null
                && updatedContact with { Channels = old.Channels } == old
                && updatedContact.Channels.SequenceEqual(old.Channels);
            contacts[change.Id] = old is not null && !unchanged
                ? updatedContact with { Version = Next(old.Version) } : updatedContact;
        }

        var phone = Text(changes.Phone, current.Phone, 32, "changes.phone");
        var phoneExtension = Text(changes.PhoneExtension, current.PhoneExtension, 10, "changes.phoneExtension");
        if (phone is null && phoneExtension is not null)
        {
            throw Invalid("changes.phoneExtension", "Dahili numara için telefon bilgisi gerekir.");
        }

        var email = Text(changes.Email, current.Email, 254, "changes.email");
        CheckEmail(email, "changes.email");
        var website = Text(changes.Website, current.Website, 2048, "changes.website");
        CheckWebsite(website);
        return new(phone, phoneExtension, email, website,
            addresses.Values.OrderBy(address => address.Id).ToArray(),
            contacts.Values.OrderBy(contact => contact.Id).ToArray());
    }

    private static bool ActiveState(string? operation, bool? current, string field) => operation switch
    {
        "upsert" => current ?? true,
        "deactivate" when current.HasValue => false,
        "reactivate" when current.HasValue => true,
        "deactivate" or "reactivate" => throw Invalid(field, "Bu şirket hesabında kayıt bulunamadı."),
        _ => throw Invalid(field, "İşlem upsert, deactivate veya reactivate olmalıdır."),
    };

    private static string Next(string version) =>
        checked(ParseVersion(version) + 1).ToString(CultureInfo.InvariantCulture);

    private static void RequireNoFields(string field, params JsonElement[] values)
    {
        if (values.Any(value => value.ValueKind != JsonValueKind.Undefined))
        {
            throw Invalid(field, "Durum değişikliğine alan güncellemesi eklenemez.");
        }
    }

    private static string Required(JsonElement value, string? previous, int maximum, string field) =>
        Text(value, previous, maximum, field)
        ?? throw Invalid(field, "Bu alan zorunludur.");

    private static string? Text(JsonElement value, string? previous, int maximum, string field)
    {
        var result = value.ValueKind switch
        {
            JsonValueKind.Undefined => previous,
            JsonValueKind.Null => null,
            JsonValueKind.String => value.GetString()?.Trim(),
            _ => throw Invalid(field, "Metin veya null bekleniyor."),
        };
        if (string.IsNullOrEmpty(result))
        {
            return null;
        }

        if (result.Length > maximum || result.Any(char.IsControl))
        {
            throw Invalid(field, $"Alan en fazla {maximum} karakter olmalı ve kontrol karakteri içermemelidir.");
        }

        return result;
    }

    private static void CheckOperations<T>(IReadOnlyList<T>? values, Func<T, Guid> identity, string field)
    {
        if (values is null || values.Count > MaximumOperationsPerCollection
            || values.Any(value => value is null || identity(value) == Guid.Empty)
            || values.Select(identity).Distinct().Count() != values.Count)
        {
            throw Invalid(field, "En fazla 100 işlem; her satırda farklı, boş olmayan kimlik gerekir. Koleksiyon null olamaz.");
        }
    }

    private static void CheckCountry(string value)
    {
        if (value.Length != 2 || value.Any(character => character is < 'A' or > 'Z'))
        {
            throw Invalid("changes.addresses.countryCode", "Ülke kodu iki Latin harfinden oluşmalıdır.");
        }
    }

    private static void CheckEmail(string? value, string field)
    {
        if (value is not null
            && (!MailAddress.TryCreate(value, out var parsed)
                || !string.Equals(parsed.Address, value, StringComparison.Ordinal)
                || !value.Contains('@')))
        {
            throw Invalid(field, "E-posta biçimi geçersiz.");
        }
    }

    private static void CheckWebsite(string? value)
    {
        if (value is not null
            && (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https")
                || string.IsNullOrEmpty(uri.Host)
                || !string.IsNullOrEmpty(uri.UserInfo)))
        {
            throw Invalid("changes.website", "Web adresi kullanıcı bilgisi içermeyen http veya https adresi olmalıdır.");
        }
    }

    private static void ValidateStored(CounterpartyContactInformation information)
    {
        Text(default, information.Phone, 32, "phone");
        Text(default, information.PhoneExtension, 10, "phoneExtension");
        CheckEmail(Text(default, information.Email, 254, "email"), "email");
        CheckWebsite(Text(default, information.Website, 2048, "website"));
        if (information.Phone is null && information.PhoneExtension is not null)
        {
            throw new InvalidOperationException("Stored extension has no phone.");
        }

        ValidateIdentities(information.Addresses.Select(address => address.Id));
        ValidateIdentities(information.Contacts.Select(contact => contact.Id));
        foreach (var address in information.Addresses)
        {
            ParseVersion(address.Version);
            if (address.Purpose is not ("billing" or "delivery" or "operational"))
            {
                throw new InvalidOperationException("Unknown stored address purpose.");
            }

            CheckCountry(address.CountryCode);
            Required(default, address.Line1, 250, "line1");
            Text(default, address.Line2, 250, "line2");
            Text(default, address.City, 100, "city");
            Text(default, address.District, 100, "district");
            Text(default, address.PostalCode, 20, "postalCode");
        }

        foreach (var contact in information.Contacts)
        {
            ParseVersion(contact.Version);
            Required(default, contact.DisplayName, 200, "displayName");
            Text(default, contact.JobTitle, 120, "jobTitle");
            Required(default, contact.Purpose, 120, "purpose");
            ValidateIdentities(contact.Channels.Select(channel => channel.Id));
            foreach (var channel in contact.Channels)
            {
                ParseVersion(channel.Version);
                if (channel.Kind is not ("phone" or "email"))
                {
                    throw new InvalidOperationException("Unknown stored channel kind.");
                }

                var value = Required(default, channel.Value, channel.Kind == "phone" ? 32 : 254, "value");
                var extension = Text(default, channel.Extension, 10, "extension");
                if (channel.Kind == "email")
                {
                    CheckEmail(value, "value");
                    if (extension is not null)
                    {
                        throw new InvalidOperationException("Stored email has an extension.");
                    }
                }
            }
        }
    }

    private static void ValidateIdentities(IEnumerable<Guid> identifiers)
    {
        var values = identifiers.ToArray();
        if (values.Any(value => value == Guid.Empty) || values.Distinct().Count() != values.Length)
        {
            throw new InvalidOperationException("Invalid stored child identities.");
        }
    }

    private static ArgumentException Invalid(string field, string message) => new(message, field);

    private sealed record StoredInformation(int SchemaVersion, CounterpartyContactInformation Information);
}
