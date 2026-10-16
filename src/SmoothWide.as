package com.srpg2.hd {
    import flash.display.DisplayObject;
    import flash.display.DisplayObjectContainer;
    import flash.events.TimerEvent;
    import flash.external.ExternalInterface;
    import flash.geom.Rectangle;
    import flash.utils.Dictionary;
    import flash.utils.Timer;
    import flash.utils.getTimer;

    public class SmoothWide {
        private static var states:Dictionary = new Dictionary(true);
        private static var active:Object;
        private static var timer:Timer;
        private static var fps:int = 60;
        private static var samples:Array = [];
        private static var ticks:int = 0;
        private static var draws:int = 0;
        private static var hudPositions:Dictionary = new Dictionary(true);

        public static function attach(main:Object):void {
            main.x = 40;
            main.scrollRect = new Rectangle(0,0,720,450);
            if(states[main])return;
            var state:Object = {main:main, previous:null, current:null, ready:false, moving:false, inUpdate:false, time:0, lastAlpha:-1};
            states[main] = state;
            active = state;
            try {
                var params:Object=main.stage.root.loaderInfo.parameters;
                if(params.hdFPS=="120")fps=120;
                if(params.hdFPS=="30")fps=30;
            }catch(error:Error){}
            if(!timer){
                timer=new Timer(1000/fps);
                timer.addEventListener(TimerEvent.TIMER,render);
                timer.start();
            }
            if(ExternalInterface.available){
                ExternalInterface.addCallback("hdDiagnostics", diagnostics);
                ExternalInterface.addCallback("hdSetFPS", setFPS);
            }
            trace("SRPG2 wide runtime: 800x450; logic 30; render target " + fps);
        }

        public static function setFPS(value:int):int {
            if(value!=30 && value!=60 && value!=120)return fps;
            fps=value;
            timer.delay=1000/fps;
            if(active){restore(active);active.ready=false;}
            return fps;
        }

        public static function layout(main:Object):void {
            var hud:DisplayObjectContainer=main.game_hud_mc as DisplayObjectContainer;
            if(!hud)return;
            for(var i:int=0;i<hud.numChildren;i++){
                var child:DisplayObject=hud.getChildAt(i);
                if(hudPositions[child]!==undefined)continue;
                hudPositions[child]=child.x;
                if(["stamina_mc","health_bar_mc","health_txt","clockBGMC","time_txt","am_pm_mc"].indexOf(child.name)>=0)child.x-=40;
                else if(["hudCashBarMC","cash_txt","statsInactiveMC","stats_btn","menuInactiveMC","menu_btn","inventoryInactiveMC","inventory_btn"].indexOf(child.name)>=0)child.x+=40;
            }
        }

        private static function capture(main:Object):Array {
            if(!main.world || !main.world.game_scene)return null;
            var scene:Object=main.world.game_scene;
            var character:Object=scene.character_container_mc;
            if(!character)return null;
            var result:Array=[{target:scene,x:scene.x,y:scene.y,scaleX:scene.scaleX,scaleY:scene.scaleY},
                              {target:character,x:character.x,y:character.y,scaleX:character.scaleX,scaleY:character.scaleY}];
            if(main.parallax_bg)result.push({target:main.parallax_bg,x:main.parallax_bg.x,y:main.parallax_bg.y,scaleX:main.parallax_bg.scaleX,scaleY:main.parallax_bg.scaleY});
            return result;
        }

        private static function restore(state:Object):void {
            if(!state.ready || !state.current)return;
            for each(var point:Object in state.current){
                point.target.x=point.x;
                point.target.y=point.y;
            }
        }

        public static function before(main:Object):void {
            var state:Object=states[main];
            if(!state)return;
            active=state;
            main.scrollRect=null;
            restore(state);
            if(main.controls_disabled)state.ready=false;
            state.previous=capture(main);
            state.inUpdate=true;
        }

        public static function pause(main:Object):void {
            var state:Object=states[main];
            if(!state)return;
            if(!state.inUpdate)restore(state);
            state.ready=false;
        }

        public static function after(main:Object):void {
            var state:Object=states[main];
            if(!state)return;
            layout(main);
            state.current=capture(main);
            state.time=getTimer();
            state.lastAlpha=-1;
            state.inUpdate=false;
            state.moving=false;
            ticks++;
            state.ready=Boolean(state.previous && state.current && !main.controls_disabled && main.world.game_scene.visible);
            if(state.ready){
                if(state.previous.length!=state.current.length)state.ready=false;
                else for(var i:int=0;i<state.current.length;i++){
                    var old:Object=state.previous[i];
                    var now:Object=state.current[i];
                    if(old.x!=now.x || old.y!=now.y)state.moving=true;
                    if(old.target!==now.target || old.scaleX!=now.scaleX || old.scaleY!=now.scaleY || Math.abs(now.x-old.x)>80 || Math.abs(now.y-old.y)>80)state.ready=false;
                }
            }
            if(fps>30 && state.ready && state.moving)apply(state,0);
        }

        private static function apply(state:Object,alpha:Number):void {
            for(var i:int=0;i<state.current.length;i++){
                var old:Object=state.previous[i];
                var now:Object=state.current[i];
                now.target.x=old.x+(now.x-old.x)*alpha;
                now.target.y=old.y+(now.y-old.y)*alpha;
            }
        }

        private static function render(event:TimerEvent):void {
            var state:Object=active;
            if(!state || !state.ready || !state.moving || fps<=30)return;
            if(state.main.controls_disabled || !state.main.world || !state.main.world.game_scene.visible || state.main.world.game_scene!==state.current[0].target){
                restore(state);
                state.ready=false;
                return;
            }
            var alpha:Number=Math.min(1,Math.max(0,(getTimer()-state.time)/(1000/30)));
            // Ruffle can deliver several overdue timer events in one host tick.
            // Avoid changing the same transforms and requesting the same picture twice.
            if(alpha<=state.lastAlpha)return;
            state.lastAlpha=alpha;
            apply(state,alpha);
            draws++;
            if(samples.length<600)samples.push({time:getTimer(),alpha:alpha,x:state.current[0].target.x,logicalX:state.current[0].x,playerX:state.current[1].target.x,logicalPlayerX:state.current[1].x});
            event.updateAfterEvent();
        }

        public static function diagnostics(reset:Boolean=false):Object {
            var state:Object=active;
            var hud:Array=[];
            if(state && state.main.game_hud_mc){
                var container:DisplayObjectContainer=state.main.game_hud_mc;
                for(var i:int=0;i<container.numChildren;i++){
                    var child:DisplayObject=container.getChildAt(i);
                    var bounds:Rectangle=child.getBounds(state.main.stage);
                    hud.push({name:child.name,x:child.x,y:child.y,width:child.width,height:child.height,originalX:hudPositions[child],left:bounds.x,right:bounds.right});
                }
            }
            var result:Object={fps:fps,logicTicks:ticks,renderUpdates:draws,samples:samples.concat(),hud:hud,
                               stageWidth:state?state.main.stage.stageWidth:0,stageHeight:state?state.main.stage.stageHeight:0,
                               stageFPS:state?state.main.stage.frameRate:0,mainX:state?state.main.x:0,
                               hudX:state&&state.main.game_hud_mc?state.main.game_hud_mc.x:0,
                               hudScale:state&&state.main.game_hud_mc?state.main.game_hud_mc.scaleX:0,
                               logicalPlayerX:state&&state.current?state.current[1].x:0,
                               logicalPlayerY:state&&state.current?state.current[1].y:0,
                               logicalSceneX:state&&state.current?state.current[0].x:0,
                               logicalSceneY:state&&state.current?state.current[0].y:0,
                               ready:state?state.ready:false,scene:state&&state.current?state.current[0].target.name:null};
            if(reset){samples=[];ticks=0;draws=0;}
            return result;
        }
    }
}
