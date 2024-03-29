﻿using PropertyChanged;
using XAT.Core;

namespace XAT.Game.Formats.Tmb;

[AddINotifyPropertyChangedInterface]
public class TmppFormat : TmbItemFormat
{
    public const string MAGIC = "TMPP";
    public override string Magic => MAGIC;

    public override int Size => 0x0C;
    public override int ExtraSize => 0;
    public override int TimelineCount => 0;

    [UserType]
    public string Path { get; set; } = string.Empty;

    public TmppFormat(TmbReadContext context)
    {
        ReadHeader(context);

        Path = context.ReadOffsetString();

    }

    public TmppFormat()
    {
    }

    public override void Serialize(TmbWriteContext context)
    {
        WriteHeader(context);

        context.WriteOffsetString(Path);
    }
}