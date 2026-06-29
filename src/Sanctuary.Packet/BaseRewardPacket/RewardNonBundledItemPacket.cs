using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class RewardNonBundledItemPacket : BaseRewardPacket, ISerializablePacket
{
    public new const byte OpCode = 2;

    public int ItemDefinitionId;
    public int Count = 1;
    public int Tint;

    public RewardNonBundledItemPacket() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(ItemDefinitionId);
        writer.Write(Count);
        writer.Write(Tint);

        return writer.Buffer;
    }
}
