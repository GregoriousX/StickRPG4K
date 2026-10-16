package com.srpg2.hd {
    import flash.events.Event;
    import flash.events.TimerEvent;
    import flash.external.ExternalInterface;
    import flash.media.Sound;
    import flash.media.SoundChannel;
    import flash.media.SoundTransform;
    import flash.utils.ByteArray;
    import flash.utils.Timer;
    import flash.utils.getQualifiedClassName;
    import flash.utils.getTimer;

    // Use embedded Sound playback when the player's PCM extraction is unavailable.
    // The original SoundManager remains the playback path in Adobe Flash.
    public class NativeMusic {
        private static var checked:Boolean = false;
        private static var enabled:Boolean = false;
        private static var manager:Object;
        private static var playlist:Array = [];
        private static var current:Object;
        private static var retiring:Array = [];
        private static var timer:Timer;
        private static var fadeStart:int = 0;
        private static var fadeDuration:int = 0;
        private static var transitions:int = 0;
        private static var completed:int = 0;
        private static var starts:int = 0;
        private static var userVolume:Number = 1;

        public static function handles(sm:Object, candidate:Object):Boolean {
            manager = sm;
            if(!checked && candidate != null) {
                var sound:Sound = candidate as Sound;
                if(!sound && candidate is Array && candidate.length > 0)
                    sound = candidate[0] as Sound;
                if(sound) {
                    checked = true;
                    var pcm:ByteArray = new ByteArray();
                    try {
                        // Ruffle returns undefined despite extract's Number signature.
                        // Inspect the stereo PCM bytes and do not pass that return to isNaN.
                        sound.extract(pcm, 1, 0);
                        enabled = pcm.length < 8;
                    } catch(error:Error) {
                        enabled = true;
                    }
                    if(ExternalInterface.available)
                        ExternalInterface.addCallback("hdMusicDiagnostics", diagnostics);
                    trace("SRPG2 music: " + (enabled ? "embedded playback fallback" : "original PCM mixer"));
                }
            }
            return enabled;
        }

        public static function playMusic(sm:Object, sound:Sound, volume:Number):void {
            start(sm, [sound], volume, false, false);
        }

        public static function playRandomSequence(sm:Object, sounds:Array, volume:Number):void {
            start(sm, sounds, volume, false, false);
        }

        public static function crossfadeMusic(sm:Object, sound:Sound):void {
            start(sm, [sound], 1, true, false);
        }

        public static function crossfadeRandomSequence(sm:Object, sounds:Array):void {
            start(sm, sounds, 1, true, false);
        }

        public static function transitionMusic(sm:Object, sound:Sound):void {
            start(sm, [sound], 1, true, true);
        }

        public static function transitionRandomSequence(sm:Object, sounds:Array):void {
            start(sm, sounds, 1, true, true);
        }

        private static function start(sm:Object, sounds:Array, volume:Number, fade:Boolean, sync:Boolean):void {
            manager = sm;
            var nextPlaylist:Array = [];
            for each(var sound:Sound in sounds) {
                if(sound)nextPlaylist.push(sound);
            }
            if(nextPlaylist.length == 0)return;
            var nextSound:Sound = nextPlaylist[int(Math.random() * nextPlaylist.length)];
            var position:Number = 0;
            if(sync && current && current.channel && nextSound.length > 0)
                position = current.channel.position % nextSound.length;
            var channel:SoundChannel = nextSound.play(position, 0, new SoundTransform(0));
            if(!channel) {
                trace("SRPG2 music: cannot start " + getQualifiedClassName(nextSound));
                return;
            }
            // Rapid scene changes must not leave older fading channels playing.
            disposeRetiring();
            if(current) {
                if(fade)retiring.push(current);
                else dispose(current);
            }
            playlist = nextPlaylist;
            current = {sound:nextSound, channel:channel, base:Math.max(0, volume), factor:fade && retiring.length > 0 ? 0 : 1};
            channel.addEventListener(Event.SOUND_COMPLETE, onComplete);
            manager.playing_music = true;
            starts++;
            if(retiring.length > 0) {
                transitions++;
                fadeStart = getTimer();
                fadeDuration = sync ? 750 : 1000;
                if(!timer) {
                    timer = new Timer(25);
                    timer.addEventListener(TimerEvent.TIMER, onFade);
                }
                timer.start();
            } else {
                fadeDuration = 0;
                if(timer)timer.stop();
            }
            refresh();
            trace("SRPG2 music: playing " + getQualifiedClassName(nextSound));
        }

        private static function onComplete(event:Event):void {
            if(!current || event.currentTarget !== current.channel)return;
            current.channel.removeEventListener(Event.SOUND_COMPLETE, onComplete);
            var sound:Sound = playlist[int(Math.random() * playlist.length)];
            var channel:SoundChannel = sound.play(0, 0, new SoundTransform(0));
            if(!channel) {
                manager.playing_music = false;
                current = null;
                return;
            }
            current.sound = sound;
            current.channel = channel;
            channel.addEventListener(Event.SOUND_COMPLETE, onComplete);
            completed++;
            starts++;
            refresh();
        }

        private static function onFade(event:TimerEvent):void {
            var alpha:Number = Math.min(1, Math.max(0, (getTimer() - fadeStart) / fadeDuration));
            if(current)current.factor = alpha;
            for each(var old:Object in retiring)old.factor = 1 - alpha;
            refresh();
            if(alpha >= 1) {
                disposeRetiring();
                fadeDuration = 0;
                timer.stop();
            }
        }

        private static function refresh():void {
            if(current)applyVolume(current);
            for each(var old:Object in retiring)applyVolume(old);
        }

        private static function applyVolume(track:Object):void {
            // Music and SFX switches control independent channels.
            track.channel.soundTransform = new SoundTransform(manager.music_intensity * track.base * track.factor * userVolume);
        }

        public static function setUserVolume(volume:Number):void {
            userVolume = Math.max(0, Math.min(1, volume));
            refresh();
        }

        public static function setMusicVolume(sm:Object, volume:Number):void {
            manager = sm;
            manager.music_intensity = Math.max(0, Math.min(1, volume));
            refresh();
        }

        public static function stopMusic(sm:Object):void {
            manager = sm;
            if(timer)timer.stop();
            fadeDuration = 0;
            disposeRetiring();
            if(current)dispose(current);
            current = null;
            playlist = [];
            manager.playing_music = false;
        }

        private static function dispose(track:Object):void {
            track.channel.removeEventListener(Event.SOUND_COMPLETE, onComplete);
            track.channel.stop();
        }

        private static function disposeRetiring():void {
            for each(var old:Object in retiring)dispose(old);
            retiring = [];
        }

        public static function diagnostics():Object {
            return {checked:checked, fallback:enabled, playing:manager ? manager.playing_music : false,
                    volume:manager ? manager.music_intensity : 0, starts:starts, completed:completed,
                    transitions:transitions, retiring:retiring.length, fading:fadeDuration > 0,
                    track:current ? getQualifiedClassName(current.sound) : null,
                    positionMs:current ? current.channel.position : 0,
                    lengthMs:current ? current.sound.length : 0,
                    channelVolume:current ? current.channel.soundTransform.volume : 0,userVolume:userVolume};
        }
    }
}
