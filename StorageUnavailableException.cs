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

using Android.Content;
using Android.Graphics;
using Android.Media;
using Android.OS;
using Android.Preferences;
using Android.Util;
using Java.IO;
using Java.Lang;
using Java.Nio;
using Java.Util.Concurrent;
using System;
using System.Collections;
using System.Collections.Generic;

using static Android.Media.MediaCodec;

namespace Net.Majorkernelpanic.Streaming
{
    public class StorageUnavailableException : IOException {

        public StorageUnavailableException(System.String message):base(message) {
            
        }

        private static long serialVersionUID = -7537890350373995089L;
    }
}