// Compile together with installer/Patcher.cs using the .NET Framework compiler.
using System;
using System.IO;

internal static class SoundtrackTests {
    private static int checks;
    private static void Check(bool condition, string name) {
        if (!condition) throw new Exception(name);
        checks++;
    }
    private static void Reject(Action operation, string name) {
        try { operation(); } catch (InvalidDataException) { checks++; return; }
        throw new Exception("Accepted " + name);
    }
    public static int Main(string[] args) {
        string directory = Path.GetFullPath(args[0]);
        if (Directory.Exists(directory)) throw new Exception("Test directory already exists.");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "album.mp3");
        // 40 MPEG-1 Layer III 192 kb/s stereo frames, plus ID3v2 and ID3v1.
        // Synthetic payload tests framing and copying, not perceptual decoding.
        const int frameLength = 626;
        byte[] mp3 = new byte[10 + 40 * frameLength + 128];
        mp3[0]=73; mp3[1]=68; mp3[2]=51; mp3[3]=3;
        for (int frame = 0; frame < 40; frame++) {
            int p = 10 + frame * frameLength;
            for (int i = 0; i < frameLength; i++) mp3[p+i]=(byte)(frame+1);
            mp3[p]=255; mp3[p+1]=251; mp3[p+2]=176; mp3[p+3]=0;
        }
        int trailer = mp3.Length-128;
        mp3[trailer]=84; mp3[trailer+1]=65; mp3[trailer+2]=71;
        File.WriteAllBytes(path, mp3);
        byte[] original = new byte[9]; original[0]=23; original[2]=47;
        Buffer.BlockCopy(BitConverter.GetBytes(5000u),0,original,3,4);
        SR4KTag sound = new SR4KTag(14,original);
        SR4KTag result = StickRpg4kPatch.SoundtrackSound(sound,path,15000);
        Check(result.Code==14 && result.Data[0]==23 && result.Data[2]==47,"Sound identity and format");
        Check(BitConverter.ToUInt32(result.Data,3)==5000,"Original sample count");
        Check(BitConverter.ToInt16(result.Data,7)==11544,"Sample-accurate trim with ten pre-roll frames");
        Check(result.Data.Length==9+17*frameLength,"Complete MP3 frames and trailing decoder padding");
        for (int i=13; i<45; i++) Check(result.Data[i]==0,"Silent reservoir primer");
        for (int i=45; i<result.Data.Length; i++)
            if (result.Data[i]!=mp3[10+3*frameLength+i-9]) throw new Exception("MP3 main data changed");
        checks++;
        Reject(delegate { StickRpg4kPatch.SoundtrackSound(sound,path,-1); },"negative trim");
        Reject(delegate { StickRpg4kPatch.SoundtrackSound(sound,path,44000); },"excerpt past end");
        Reject(delegate { StickRpg4kPatch.SoundtrackSound(new SR4KTag(82,original),path,15000); },"non-sound tag");
        original[2]=46;
        Reject(delegate { StickRpg4kPatch.SoundtrackSound(sound,path,15000); },"mono original");
        original[2]=47; mp3[7]=255; File.WriteAllBytes(path,mp3);
        Reject(delegate { StickRpg4kPatch.SoundtrackSound(sound,path,15000); },"malformed ID3 size");
        mp3[7]=0; mp3[12]=180; File.WriteAllBytes(path,mp3);
        Reject(delegate { StickRpg4kPatch.SoundtrackSound(sound,path,15000); },"unsupported sample rate");
        mp3[12]=176;
        Array.Resize(ref mp3,mp3.Length-200); File.WriteAllBytes(path,mp3);
        Reject(delegate { StickRpg4kPatch.SoundtrackSound(sound,path,15000); },"truncated MP3 frame");
        Console.WriteLine("PASS: " + checks + " soundtrack checks.");
        return 0;
    }
}
