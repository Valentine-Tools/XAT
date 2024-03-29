﻿using PropertyChanged;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using XAT.Game.Formats.Utils;

namespace XAT.Game.Formats.Tmb;

[AddINotifyPropertyChangedInterface]
public class TmbFormat
{
    public const string MAGIC = "TMLB";

    public TmdhFormat Header { get; set; }

    [DependsOn(nameof(FaceLibrary))]
    public bool HasFaceLibrary
    {
        get => FaceLibrary != null;
        set
        {
            if (value && FaceLibrary == null)
            {
                FaceLibrary = new TmppFormat();
            }
            else if (!value && FaceLibrary != null)
            {
                FaceLibrary = null;
            }
        }
    }
    public TmppFormat? FaceLibrary { get; set; }

    [DependsOn(nameof(TmfcEntry))]
    public bool HasTmfc
    {
        get => TmfcEntry != null;
        set
        {
            if (value && TmfcEntry == null)
            {
                TmfcEntry = new TmfcFormat();
            }
            else if (!value && TmfcEntry != null)
            {
                TmfcEntry = null;
            }
        }
    }
    public TmfcFormat? TmfcEntry { get; set; }

    public TmalFormat ActorList { get; set; }

    public TmbFormat(BinaryReader reader)
    {
        var startPos = reader.BaseStream.Position;

        string magic = reader.ReadEncodedString(4);
        if (magic != MAGIC)
            throw new Exception($"Invalid file - magic incorrect. Expected {MAGIC}, got {magic}.");

        int size = reader.ReadInt32();

        int numEntries = reader.ReadInt32();

        // We read every tmb item and do id resolution
        // id resolution is not complete until a full pass has been made
        TmbReadContext readContext = new(reader, this);
        Queue<TmbItemFormat> items = new();
        for (int i = 0; i < numEntries; ++i)
        {
            var startPosition = reader.BaseStream.Position;
            readContext.SubDocumentStartPosition = startPosition;
            TmbItemFormat entry = TmbItemFormat.ParseItem(readContext);
            items.Enqueue(entry);

            if (entry is TmbItemWithIdFormat withId)
                readContext.GetPointerAtId<TmbItemWithIdFormat>(withId.Id).Item= withId;

            reader.BaseStream.Seek(startPosition + entry.Size, SeekOrigin.Begin);
        }

        reader.BaseStream.Seek(startPos + size, SeekOrigin.Begin);

        // Header
        if (items.Dequeue() is TmdhFormat tmdh)
        {
            Header = tmdh;
        }
        else
        {
            throw new Exception("Expected first entry to be TMDH");
        }

        // Facial Library - optional
        if (items.Peek() is TmppFormat)
        {
            FaceLibrary = items.Dequeue() as TmppFormat;
        }

        // Actor list / header
        if (items.Dequeue() is TmalFormat tmal)
        {
            ActorList = tmal;
        }
        else
        {
            throw new Exception("Expected entry to be TMAL");
        }

        foreach(var item in items)
        {
            // Tmfc
            if (item is TmfcFormat tmfc)
            {
                TmfcEntry = tmfc;
            }
        }
    }
    public void Serialize(BinaryWriter writer)
    {
        var startPos = writer.BaseStream.Position;
        writer.WriteEncodedString(MAGIC);
        writer.Write((int)0);

        List<TmbItemFormat> items = new();

        short currentId = 1;
        Header.Id = currentId++;
        items.Add(Header);

        if(FaceLibrary != null)
            items.Add(FaceLibrary);

        items.Add(ActorList);

        // Now we crawl from the actor header finding all the actors, tracks and entries - we assign new ids as we go.
        {
            foreach (var actorPointer in ActorList.Actors)
            {
                actorPointer.Item.Id = currentId++;
                items.Add(actorPointer.Item);
            }

            foreach (var actorPointer in ActorList.Actors)
            {
                foreach (var trackPointer in actorPointer.Item.Tracks)
                {
                    if (trackPointer.Item == null || trackPointer.Item is not TmtrFormat)
                        throw new Exception("Unresolved track");


                    items.Add(trackPointer.Item);
                    trackPointer.Item.Id = currentId++;
                }
            }

            foreach (var actor in ActorList.Actors)
            {
                foreach (var trackPointer in actor.Item.Tracks)
                {
                    if (trackPointer.Item == null || trackPointer.Item is not TmtrFormat)
                        throw new Exception("Unresolved track");

                    foreach (var entryPointer in trackPointer.Item.Entries)
                    {
                        if (entryPointer.Item == null)
                            throw new Exception("Unresolved entry");

                        items.Add(entryPointer.Item);
                        entryPointer.Item.Id = currentId++;
                    }
                }
            }
        }

        if (TmfcEntry != null)
            items.Add(TmfcEntry);

        int itemLength = items.Sum(x => x.Size);
        int extraLength = items.Sum(x => x.ExtraSize);
        int timelineLength = items.Sum(x => x.TimelineCount) * sizeof(short);
        var context = new TmbWriteContext(itemLength, extraLength, timelineLength, this);

        writer.Write(items.Count);
        foreach (var item in items)
        {
            context.SubDocumentStartPosition = context.Writer.BaseStream.Position;
            item.Serialize(context);

            var writtenSize = context.Writer.BaseStream.Position - context.SubDocumentStartPosition;
            if (writtenSize != item.Size)
                throw new Exception($"Item did not write correct amount of data: {item.Magic} expected {item.Size} but got {writtenSize}.");
        }

        // Put it all together
        writer.Write(context.Writer.ToArray());
        writer.Write(context.ExtraWriter.ToArray());
        writer.Write(context.TimelineWriter.ToArray());
        writer.Write(context.StringWriter.ToArray());

        // Fix size
        var endPos = writer.BaseStream.Position;
        writer.BaseStream.Seek(startPos + 4, SeekOrigin.Begin);
        writer.Write((int)(endPos - startPos));
        writer.BaseStream.Seek(endPos, SeekOrigin.Begin);
    }

    public byte[] ToBytes()
    {
        using var stream = new MemoryStream();
        this.Serialize(new BinaryWriter(stream));
        return stream.ToArray();
    }

    public void ToFile(string filePath)
    {
        using var sklbStream = File.Open(filePath, FileMode.Create);
        this.Serialize(new BinaryWriter(sklbStream));
    }

    public static TmbFormat FromFile(string filePath)
    {
        using var stream = File.Open(filePath, FileMode.Open);
        return new TmbFormat(new BinaryReader(stream));
    }

    public static TmbFormat FromBytes(byte[] data)
    {
        using var stream = new MemoryStream(data);
        return new TmbFormat(new BinaryReader(stream));
    }
}