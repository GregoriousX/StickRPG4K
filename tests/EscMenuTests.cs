using System;
using System.Diagnostics;
using StickRpg4k;
internal static class EscMenuTests {
    private static int checks;
    private static void Check(bool value,string name){if(!value)throw new Exception(name);checks++;}
    public static int Main(){
        foreach(int message in new[]{0x100,0x101,0x104,0x105}) {
            Check(EscMenuBridge.RoutesToMenu(0,27,222,222,message),"Game Escape routed");
            Check(!EscMenuBridge.RoutesToMenu(0,27,333,222,message),"Other apps unaffected");
            Check(!EscMenuBridge.RoutesToMenu(0,77,222,222,message),"Other keys unaffected");
            Check(!EscMenuBridge.RoutesToMenu(-1,27,222,222,message),"Hook chain protocol");
        }
        Check(!EscMenuBridge.RoutesToMenu(0,27,222,222,0x200),"Non-key messages unaffected");
        uint down=unchecked((uint)EscMenuBridge.KeyMessage(false).ToInt64());
        uint up=unchecked((uint)EscMenuBridge.KeyMessage(true).ToInt64());
        Check((down&65535)==1,"Key repetition count");
        Check((down&0xc0000000)==0 && (up&0xc0000000)==0xc0000000,"Release and previous-key flags");
        Check(((down>>16)&255)!=0,"F13 scan code mapped");
        for(int i=0;i<3;i++)using(new EscMenuBridge(Process.GetCurrentProcess().Id)){}
        Check(true,"Native hook creation and disposal without keyboard injection");
        Console.WriteLine("PASS: "+checks+" Escape routing checks.");return 0;
    }
}
