using System.Security.Cryptography;
using System.Text;

namespace Pingy.Core;

public static class ProfileMigration
{
    // Fingerprints of the three unedited factory entries from v1.0. Enabled is intentionally
    // excluded: selecting a sample does not make it a customized host. Edited entries survive.
    private static readonly HashSet<string> LegacySamples =
    [
        "ADA6EB17D445D21568E2E1EB1D393890B646B3F2611C24B14F67341BECA8213B",
        "EB4F2736727C15A5EDC522C40D0B2BFB1E0534BC44C487F5225CBBAA45D8AAE1",
        "4FBE6413FCA55C97799FEA2233C6FE9589BB3D8A5823784B8A91EF5FA7029261"
    ];

    public static int RemoveUnmodifiedSamples(AppConfig config) => config.Hosts.RemoveAll(host =>
        LegacySamples.Contains(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join('\u001f', host.Id.ToString("D"), host.Name, host.Address, host.Group, host.Description))))));
}
