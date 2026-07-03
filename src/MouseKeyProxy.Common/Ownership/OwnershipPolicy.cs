namespace MouseKeyProxy.Common.Ownership;

public enum OwnedCapability { Input, Hooks, Clip, Send }

public class OwnershipPolicy
{
    public bool IsOwned(OwnedCapability cap, bool isAgent)
    {
        // Agent owns, Service not
        return isAgent;
    }
}
