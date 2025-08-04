using MessagePack.Resolvers;
using MessagePack;

namespace SeacoreCommon.Utilities
{
    public static class SerializerOptionsProvider
    {
        public static readonly MessagePackSerializerOptions Options = MessagePackSerializerOptions.Standard
            .WithResolver(CompositeResolver.Create(
                DynamicUnionResolver.Instance,
                StandardResolver.Instance))
            .WithSecurity(MessagePackSecurity.UntrustedData);
    }
}
