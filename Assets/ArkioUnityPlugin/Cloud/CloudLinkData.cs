namespace Arkio
{
    // Structing data that needs to be stored 
    // for a single CloudExchangeLink
    public struct CloudLinkData
    {
        // The encryption key
        // as a base 64 encoded string
        public string LinkKey;

        // Name of the link
        public string Name;
    }
}

