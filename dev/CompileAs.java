import com.jpexs.decompiler.flash.SWF;
import com.jpexs.decompiler.flash.abc.ABC;
import com.jpexs.decompiler.flash.abc.avm2.parser.script.ActionScript3Parser;
import java.io.*;
import java.nio.file.*;

public class CompileAs {
    public static void main(String[] args) throws Exception {
        SWF swf;
        try (InputStream input=Files.newInputStream(Path.of(args[0]))) {swf=new SWF(input,true);}
        ABC abc=new ABC(null);
        var index=swf.getAbcIndex();
        index.selectAbc(abc);
        new ActionScript3Parser(index).addScript(Files.readString(Path.of(args[1])),args[1],0,0,null,abc);
        try (OutputStream output=Files.newOutputStream(Path.of(args[2]))) {abc.saveToStream(output);}
        System.out.println("Compiled " + args[2]);
    }
}
