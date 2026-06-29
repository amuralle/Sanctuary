using System.Collections.Generic;

using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class ExecuteScriptWithStringParamsPacket : BaseUiPacket, ISerializablePacket
{
    public new const byte OpCode = 8;

    public string Script = string.Empty;

    public List<int> Params = new();
    public List<string> StringParams = new();

    public ExecuteScriptWithStringParamsPacket() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(Script);
        writer.Write(Params);

        writer.Write(StringParams.Count);

        foreach (var param in StringParams)
            writer.Write(param);

        return writer.Buffer;
    }
}
