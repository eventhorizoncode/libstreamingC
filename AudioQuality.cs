/*
 * Copyright (C) 2011-2015 GUIGUI Simon, fyhertz@gmail.com
 *
 * This file is part of libstreaming (https://github.com/fyhertz/libstreaming)
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 * 
 *     http://www.apache.org/licenses/LICENSE-2.0
 * 
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using Java.Lang;
using System;

namespace Net.Majorkernelpanic.Streaming
{
    /**
     * A class that represents the quality of an audio stream.
     */
    public class AudioQuality
    {

        /** Default audio stream quality. */
        public static AudioQuality DEFAULT_AUDIO_QUALITY = new AudioQuality(8000, 32000);

        /**	Represents a quality for a video stream. */
        public AudioQuality() { }

        /**
         * Represents a quality for an audio stream.
         * @param samplingRate The sampling rate
         * @param bitRate The bitrate in bit per seconds
         */
        public AudioQuality(int samplingRate, int bitRate) {
            this.samplingRate = samplingRate;
            this.bitRate = bitRate;
        }

        public int samplingRate = 0;
        public int bitRate = 0;

        public bool equals(AudioQuality quality) {
            if (quality == null) return false;
            return (quality.samplingRate == this.samplingRate &&
                    quality.bitRate == this.bitRate);
        }

        public AudioQuality clone() {
            return new AudioQuality(samplingRate, bitRate);
        }

        public static AudioQuality parseQuality(System.String str) {
            AudioQuality quality = DEFAULT_AUDIO_QUALITY.clone();
            if (str != null) {
                System.String[] config = str.Split('-');
                try {
                    quality.bitRate = Integer.ParseInt(config[0]) * 1000; // conversion to bit/s
                    quality.samplingRate = Integer.ParseInt(config[1]);
                }
                catch (IndexOutOfBoundsException ignore) { }
            }
            return quality;
        }

    }


}