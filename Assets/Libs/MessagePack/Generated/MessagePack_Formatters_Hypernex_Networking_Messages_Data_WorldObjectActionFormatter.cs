// <auto-generated>
// THIS (.cs) FILE IS GENERATED BY MPC(MessagePack-CSharp). DO NOT CHANGE IT.
// </auto-generated>

#pragma warning disable 618
#pragma warning disable 612
#pragma warning disable 414
#pragma warning disable 168
#pragma warning disable CS1591 // document public APIs

#pragma warning disable SA1403 // File may only contain a single namespace
#pragma warning disable SA1649 // File name should match first type name

namespace MessagePack.Formatters.Hypernex.Networking.Messages.Data
{

    public sealed class WorldObjectActionFormatter : global::MessagePack.Formatters.IMessagePackFormatter<global::Hypernex.Networking.Messages.Data.WorldObjectAction>
    {
        public void Serialize(ref global::MessagePack.MessagePackWriter writer, global::Hypernex.Networking.Messages.Data.WorldObjectAction value, global::MessagePack.MessagePackSerializerOptions options)
        {
            writer.Write((global::System.Int32)value);
        }

        public global::Hypernex.Networking.Messages.Data.WorldObjectAction Deserialize(ref global::MessagePack.MessagePackReader reader, global::MessagePack.MessagePackSerializerOptions options)
        {
            return (global::Hypernex.Networking.Messages.Data.WorldObjectAction)reader.ReadInt32();
        }
    }
}

#pragma warning restore 168
#pragma warning restore 414
#pragma warning restore 618
#pragma warning restore 612

#pragma warning restore SA1403 // File may only contain a single namespace
#pragma warning restore SA1649 // File name should match first type name