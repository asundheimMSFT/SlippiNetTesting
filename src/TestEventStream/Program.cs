using Slippi.NET;
using Slippi.NET.Slp.EventStream;
using Slippi.NET.Slp.EventStream.Types;
using Slippi.NET.Slp.Parser;
using Slippi.NET.Slp.Parser.Types;
using Slippi.NET.Stats;
using Slippi.NET.Types;
using Slippi.NET.Tests;
using System.Diagnostics.CodeAnalysis;

namespace TestEventStream;

/// <summary>
/// Port of <see cref="RealtimeTests.ReadingLastFinalizedFrameFromSlpStream_ShouldNeverDecrease"/> 
/// to make it easier to run as a standalone process.
/// </summary>
internal class Program
{
    public static void Main(string[] args)
    {
        const string testFile = "slp/finalizedFrame.slp";
        var stream = new SlpEventStream(new SlpStreamSettings() { Mode = SlpStreamModes.MANUAL });
        var parser = new SlpParser(new SlpParserOptions());

        int lastFinalizedFrame = (int)Frames.FIRST - 1;
        int parserLastFinalizedFrame = (int)Frames.FIRST - 1;

        // The game mode should be online
        var game = new SlippiGame(testFile, new StatOptions());
        var settings = game.GetSettings();
        Assert(GameMode.ONLINE == settings?.GameMode, "Should be ONLINE");

        stream.OnCommand += (sender, args) =>
        {
            parser.HandleCommand(args.Command, args.Payload);
            if (args.Command == Command.FRAME_BOOKEND)
            {
                var payload = args.Payload as FrameBookendPayload;
                Assert(payload is not null);

                var bookend = payload.FrameBookend;
                Assert(bookend.LatestFinalizedFrame is not null, "Shouldn't emit FRAME_BOOKEND without finalizing frames");
                Assert((int)Frames.FIRST - 1 != bookend.LatestFinalizedFrame, "Shouldn't finalize nonexistant frame");
                Assert(bookend.LatestFinalizedFrame >= lastFinalizedFrame, "Shouldn't finalize a frame older than most recent finalized frame");
                Assert(bookend.LatestFinalizedFrame >= bookend.Frame - SlpParser.MAX_ROLLBACK_FRAMES, "Shouldn't finalize a frame this old");
                lastFinalizedFrame = bookend.LatestFinalizedFrame.Value;
            }
        };

        parser.OnFinalizedFrame += (sender, frameEntry) =>
        {
            Assert(frameEntry is not null);
            Assert(frameEntry.Frame is not null);
            Assert(parserLastFinalizedFrame != frameEntry.Frame);
            Assert(parserLastFinalizedFrame + 1 == frameEntry.Frame);
            parserLastFinalizedFrame = frameEntry.Frame.Value;
        };

        PipeFileContents(testFile, stream);

        // The last finalized frame should be the same as what's recorded in the metadata
        var metadata = game.GetMetadata();
        Assert(metadata is not null);
        Assert(metadata.LastFrame == lastFinalizedFrame);
    }

    private static void PipeFileContents(string filename, SlpEventStream destination)
    {
        using var readStream = new FileStream(filename, FileMode.Open, FileAccess.Read);

        const int chunkSize = 1; // This is good for validating that we buffer everything correctly
        Span<byte> buffer = stackalloc byte[chunkSize];
        while (readStream.Position < readStream.Length)
        {
            if (chunkSize < readStream.Length - readStream.Position)
            {
                readStream.ReadExactly(buffer);
                destination.Write(buffer);
            }
            else
            {
                Span<byte> remainder = stackalloc byte[(int)(readStream.Length - readStream.Position)];
                readStream.ReadExactly(remainder);
                destination.Write(remainder);

                return;
            }
        }
    }

    private static void Assert([DoesNotReturnIf(false)] bool expected, string? message = null)
    {
        if (!expected)
        {
            throw new Exception(message);
        }
    }
}
