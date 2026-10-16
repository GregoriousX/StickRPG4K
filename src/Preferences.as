package com.srpg2.hd {
    import flash.display.*;
    import flash.events.*;
    import flash.geom.Rectangle;
    import flash.media.*;
    import flash.net.SharedObject;
    import flash.system.ApplicationDomain;
    import flash.text.*;
    import flash.utils.Dictionary;
    import flash.utils.getQualifiedClassName;

    public class Preferences {
        public static var musicGain:Number = 1;
        public static var sfxGain:Number = 1;
        private static var loaded:Boolean = false;
        private static var settings:SharedObject;
        private static var main:Object;
        private static var effects:Array = [];
        private static var interrupted:Object;
        private static var phones:Dictionary = new Dictionary(true);

        private static function clamp(value:Number):Number {
            return isNaN(value) ? 1 : Math.max(0,Math.min(1,value));
        }
        private static function load():void {
            if(loaded)return;
            loaded=true;
            try {
                settings=SharedObject.getLocal("srpg2Preferences");
                if(settings.data.music is Number)musicGain=clamp(settings.data.music);
                if(settings.data.sfx is Number)sfxGain=clamp(settings.data.sfx);
            }catch(error:Error){}
            updateMusic();
        }
        private static function save():void {
            if(!settings)return;
            try {settings.data.music=musicGain;settings.data.sfx=sfxGain;settings.flush();}catch(error:Error){}
        }
        private static function updateMusic():void {
            var music:Object=ApplicationDomain.currentDomain.getDefinition("com.srpg2.hd.NativeMusic");
            music.setUserVolume(musicGain);
        }
        public static function setMusic(value:Number):void {
            load();musicGain=clamp(value);updateMusic();save();
        }
        public static function setSfx(value:Number):void {
            load();sfxGain=clamp(value);refreshSfx();save();
        }
        public static function attach(game:Object):void {
            load();main=game;
            game.stage.addEventListener(KeyboardEvent.KEY_DOWN,keyDown,false,10000,true);
        }
        private static var forwarding:Boolean = false;
        private static function keyDown(event:KeyboardEvent):void {
            // The Windows launcher maps physical Escape to F13 before Ruffle's
            // fullscreen shortcut. Escape also works in a non-desktop player.
            if(forwarding || (event.keyCode!=124 && event.keyCode!=27))return;
            if(toggleMenu()) {
                event.preventDefault();event.stopImmediatePropagation();
            } else if(event.keyCode==124) {
                // Nothing to leave: hand the game its own Escape (e.g. ATM Cancel).
                event.stopImmediatePropagation();
                var stage:Stage=main.stage;
                var target:InteractiveObject=stage.focus ? stage.focus : stage;
                forwarding=true;
                try {target.dispatchEvent(new KeyboardEvent(KeyboardEvent.KEY_DOWN,true,false,27,27));}
                finally {forwarding=false;}
            }
        }
        // Instance names and classes the original game uses for Leave/Close.
        private static const LEAVE_NAMES:Object={leave_btn:true,close_btn:true,id_close_btn:true,
            receipt_close_btn:true,closeAction:true,LeaveButton:true,leaveButton:true};
        private static function findLeave(node:DisplayObject):InteractiveObject {
            if(!node.visible)return null;
            var found:InteractiveObject=null;
            var container:DisplayObjectContainer=node as DisplayObjectContainer;
            if(container && !(node is SimpleButton)) {
                // Later children render on top; keep the last match.
                for(var i:int=0;i<container.numChildren;i++) {
                    var child:InteractiveObject=findLeave(container.getChildAt(i));
                    if(child)found=child;
                }
            }
            if(found)return found;
            var item:InteractiveObject=node as InteractiveObject;
            if(!item || !item.mouseEnabled || (item is SimpleButton && !SimpleButton(item).enabled))return null;
            return LEAVE_NAMES[item.name]===true || getQualifiedClassName(item)=="leave_btn" ? item : null;
        }
        public static function toggleMenu():Boolean {
            if(!main || !main.stage)return false;
            var leave:InteractiveObject=findLeave(main.stage);
            if(leave) {
                main.stage.focus=null;
                trace("SRPG2 escape: "+leave.name+" ("+getQualifiedClassName(leave)+")");
                leave.dispatchEvent(new MouseEvent(MouseEvent.CLICK));
                return true;
            }
            if(main.XPhone && !main.controls_disabled && main.currentLabel=="city") {
                main.stage.focus=null;main.showXPhone();
                return true;
            }
            trace("SRPG2 escape: passed to game");
            return false;
        }
        public static function changeScreen(phone:Object,event:MouseEvent):void {
            phone.SaveScreen.visible=event.currentTarget===phone.SaveButton;
            phone.SoundScreen.visible=event.currentTarget===phone.SoundButton;
            phone.CheatScreen.visible=event.currentTarget===phone.CheatButton;
            phone.LoginScreen.visible=false;phone.RegisterScreen.visible=false;
        }
        private static function volumeSlider(template:Object,name:String,value:Number,change:Function,width:Number):Sprite {
            // QualitySlider lives in Minigames.swf's child domain, which this
            // dat.swf module cannot resolve by name; reuse the phone's own class.
            var sliderClass:Class=template.constructor as Class;
            var track:Object=new sliderClass();
            var trackBounds:Rectangle=track.getBounds(track);
            var scale:Number=width/trackBounds.width;
            track.scaleX=scale;track.scaleY=scale;
            track.x=-trackBounds.x*scale;track.y=-trackBounds.y*scale;
            var holder:Sprite=new Sprite();holder.name=name;holder.addChild(DisplayObject(track));
            var dragStage:Stage;
            var draw:Function=function(level:Number):void {track.handle.x=5+123*level;};
            var move:Function=function(event:MouseEvent):void {
                var level:Number=Math.round(clamp((track.mouseX-5)/123)*100)/100;
                draw(level);change(level);event.updateAfterEvent();
            };
            var up:Function=function(event:MouseEvent):void {
                if(dragStage){dragStage.removeEventListener(MouseEvent.MOUSE_MOVE,move);dragStage.removeEventListener(MouseEvent.MOUSE_UP,up);dragStage=null;}
            };
            holder.addEventListener(MouseEvent.MOUSE_DOWN,function(event:MouseEvent):void {
                move(event);dragStage=holder.stage;dragStage.addEventListener(MouseEvent.MOUSE_MOVE,move);dragStage.addEventListener(MouseEvent.MOUSE_UP,up);
            });
            holder.addEventListener(Event.REMOVED_FROM_STAGE,function(event:Event):void {up(null);});
            holder.buttonMode=true;holder.mouseChildren=false;draw(value);
            return holder;
        }
        private static function staticLabel(screen:MovieClip,y:Number):DisplayObject {
            for(var i:int=0;i<screen.numChildren;i++) {
                var child:DisplayObject=screen.getChildAt(i);
                if(getQualifiedClassName(child)=="flash.text::StaticText" && Math.abs(child.y-y)<1)return child;
            }
            throw new Error("Options label not found at y="+y);
        }
        // Keep the original Options screen (FULLSCREEN switch, QUALITY slider,
        // version); only the MUSIC/SFX on-off switches become volume sliders.
        public static function configurePhone(phone:Object,qualityChanged:Function,fullscreen:Function):void {
            load();if(phones[phone])return;phones[phone]=true;
            phone.LoginScreen.visible=false;phone.RegisterScreen.visible=false;
            var screen:MovieClip=phone.SoundScreen;
            try {
                var rows:Array=[
                    {label:staticLabel(screen,-74),toggle:screen.MusicSwitch,name:"musicVolume",value:musicGain,change:setMusic},
                    {label:staticLabel(screen,-48),toggle:screen.SFXSwitch,name:"sfxVolume",value:sfxGain,change:setSfx}];
                var labelRight:Number=-40;var gap:Number=6;
                for each(var row:Object in rows) {
                    var toggle:Rectangle=row.toggle.getBounds(screen);
                    var left:Number=labelRight+gap;
                    row.track=volumeSlider(screen.slider,row.name,row.value,row.change,toggle.right-left);
                    var trackBounds:Rectangle=row.track.getBounds(row.track);
                    row.track.x=left;
                    row.track.y=toggle.y+toggle.height/2-trackBounds.height/2;
                }
            } catch(error:Error) {
                // Never block the phone: keep the original sound screen instead.
                trace("OFFLINE_OPTIONS failed: "+error);
                return;
            }
            for each(row in rows) {
                var text:DisplayObject=row.label;
                text.x+=labelRight-text.getBounds(screen).right;
                row.toggle.visible=false;row.toggle.mouseEnabled=false;
                screen.addChild(row.track);
            }
            trace("OFFLINE_OPTIONS ready music="+musicGain+" sfx="+sfxGain+" quality="+phone.constructor.quality);
        }

        private static function play(sm:Object,sound:Sound,volume:Number,pan:Number,loops:int=0):Object {
            load();var channel:SoundChannel=sound.play(0,loops,new SoundTransform(sm.volume*volume*sfxGain,pan));
            if(!channel)return null;
            var track:Object={manager:sm,channel:channel,base:volume,pan:pan};effects.push(track);
            channel.addEventListener(Event.SOUND_COMPLETE,effectComplete);return track;
        }
        public static function playSound(sm:Object,sound:Sound,volume:Number,pan:Number):void {play(sm,sound,volume,pan);}
        public static function playInterruptableSound(sm:Object,sound:Sound,volume:Number,pan:Number):void {
            interruptSound();interrupted=play(sm,sound,volume,pan);
        }
        public static function playInterruptableLoop(sm:Object,sound:Sound,volume:Number,pan:Number,intro:Sound):void {
            interruptSound();interrupted=play(sm,intro ? intro : sound,volume,pan,intro ? 0 : 99);
            if(interrupted && intro)interrupted.next=sound;
        }
        private static function effectComplete(event:Event):void {
            for(var i:int=effects.length-1;i>=0;i--) {
                var track:Object=effects[i];
                if(track.channel!==event.currentTarget)continue;
                track.channel.removeEventListener(Event.SOUND_COMPLETE,effectComplete);effects.splice(i,1);
                if(track===interrupted) {
                    interrupted=null;
                    if(track.next)interrupted=play(track.manager,track.next,track.base,track.pan,99);
                }
            }
        }
        public static function interruptSound():void {
            if(!interrupted)return;
            interrupted.channel.removeEventListener(Event.SOUND_COMPLETE,effectComplete);interrupted.channel.stop();
            var index:int=effects.indexOf(interrupted);if(index>=0)effects.splice(index,1);interrupted=null;
        }
        public static function refreshSfx():void {
            for each(var track:Object in effects)track.channel.soundTransform=new SoundTransform(track.manager.volume*track.base*sfxGain,track.pan);
        }
        public static function diagnostics():Object {
            load();var volumes:Array=[];
            for each(var track:Object in effects)volumes.push(track.channel.soundTransform.volume);
            return {music:musicGain,sfx:sfxGain,effects:effects.length,effectVolumes:volumes};
        }
    }
}
