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
        private static function label(text:String,x:Number,y:Number,width:Number,size:int=11,color:uint=0x172229):TextField {
            var field:TextField=new TextField();field.text=text;
            field.x=x;field.y=y;field.width=width;field.height=size+9;
            field.selectable=false;field.mouseEnabled=false;
            field.defaultTextFormat=new TextFormat("_sans",size,color);
            field.setTextFormat(field.defaultTextFormat);
            return field;
        }
        private static function button(text:String,x:Number,y:Number,width:Number,action:Function):Sprite {
            var item:Sprite=new Sprite();item.x=x;item.y=y;
            item.graphics.beginFill(0x36464e);item.graphics.drawRoundRect(0,0,width,18,5);item.graphics.endFill();
            item.addChild(label(text,3,0,width-4,10,0xffffff));item.buttonMode=true;item.mouseChildren=false;
            item.addEventListener(MouseEvent.CLICK,function(event:MouseEvent):void {action();});
            return item;
        }
        private static function slider(panel:Sprite,template:Object,name:String,title:String,y:Number,value:Number,change:Function,width:Number,steps:int=100):void {
            if(title!="")panel.addChild(label(title,9,y,width-18,11));
            // QualitySlider lives in Minigames.swf's child domain, which this
            // dat.swf module cannot resolve by name; reuse the phone's own class.
            var sliderClass:Class=template.constructor as Class;
            var track:Object=new sliderClass();track.name=name;
            var trackBounds:Rectangle=track.getBounds(track);
            var scale:Number=(width-20)/trackBounds.width;
            track.scaleX=scale;track.scaleY=scale;
            track.x=10-trackBounds.x*scale;track.y=y+23-trackBounds.y*scale;
            var number:TextField=label("",width-45,y,40,10);panel.addChild(number);
            var dragStage:Stage;
            var draw:Function=function(level:Number):void {
                track.handle.x=5+123*level;
                number.text=steps==100 ? Math.round(level*100)+"%" : "";
            };
            var move:Function=function(event:MouseEvent):void {
                var level:Number=Math.round(clamp((track.mouseX-5)/123)*steps)/steps;
                draw(level);change(level);event.updateAfterEvent();
            };
            var up:Function=function(event:MouseEvent):void {
                if(dragStage){dragStage.removeEventListener(MouseEvent.MOUSE_MOVE,move);dragStage.removeEventListener(MouseEvent.MOUSE_UP,up);dragStage=null;}
            };
            track.addEventListener(MouseEvent.MOUSE_DOWN,function(event:MouseEvent):void {
                move(event);dragStage=track.stage;dragStage.addEventListener(MouseEvent.MOUSE_MOVE,move);dragStage.addEventListener(MouseEvent.MOUSE_UP,up);
            });
            track.addEventListener(Event.REMOVED_FROM_STAGE,function(event:Event):void {up(null);});
            track.buttonMode=true;track.mouseChildren=false;draw(value);panel.addChild(track);
        }
        public static function configurePhone(phone:Object,qualityChanged:Function,fullscreen:Function):void {
            load();if(phones[phone])return;phones[phone]=true;
            phone.LoginScreen.visible=false;phone.RegisterScreen.visible=false;
            var screen:MovieClip=phone.SoundScreen;
            var panel:Sprite=new Sprite();panel.name="hdOptions";
            // SoundScreen sits above the phone's tab buttons; the panel's
            // background must not swallow clicks meant for Save/Cheats.
            panel.mouseEnabled=false;
            var i:int;
            try {
                var bounds:Rectangle=screen.getBounds(screen);
                trace("OFFLINE_OPTIONS bounds="+bounds+" phone="+phone.getBounds(phone));
                panel.x=bounds.x;panel.y=bounds.y;
                var width:Number=Math.max(170,bounds.width);var height:Number=Math.max(190,bounds.height);
                panel.graphics.beginFill(0xc6cacb);panel.graphics.drawRect(0,0,width,height);panel.graphics.endFill();
                panel.graphics.beginFill(0x00aabd);panel.graphics.drawRect(0,0,width,18);panel.graphics.endFill();
                panel.addChild(label("Options",width/2-22,1,70,11,0xffffff));
                slider(panel,screen.slider,"musicVolume","MUSIC",22,musicGain,setMusic,width);
                slider(panel,screen.slider,"sfxVolume","SOUND EFFECTS",72,sfxGain,setSfx,width);
                var fullscreenButton:Sprite=button("Fullscreen",10,125,width-20,function():void {fullscreen(null);});
                panel.addChild(fullscreenButton);
                panel.addChild(label("QUALITY",width/2-28,146,70,12));
                var qualityNames:Array=["Low","Med.","High","Ultra"];
                var qualityClass:Object=phone.constructor;
                for(i=0;i<4;i++)panel.addChild(label(qualityNames[i].toUpperCase(),10+i*(width-16)/4,166,(width-20)/4,10));
                slider(panel,screen.slider,"qualitySlider","",166,qualityClass.quality/3,function(value:Number):void {
                    screen.slider.setSliderValue(Math.round(value*3));qualityChanged(null);
                },width,3);
            } catch(error:Error) {
                // Never block the phone: keep the original sound screen instead.
                trace("OFFLINE_OPTIONS failed: "+error);
                return;
            }
            for(i=0;i<screen.numChildren;i++)screen.getChildAt(i).visible=false;
            screen.addChild(panel);
            trace("OFFLINE_OPTIONS ready music="+musicGain+" sfx="+sfxGain+" quality="+qualityClass.quality);
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
