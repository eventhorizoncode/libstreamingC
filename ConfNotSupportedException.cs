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

using Android.Media;
using Android.OS;
using Android.Util;
using Java.IO;
using Java.Lang;
using System;
using System.Collections;
using System.Collections.Generic;

namespace Net.Majorkernelpanic.Streaming
{

    public class ConfNotSupportedException : RuntimeException {
	
	public ConfNotSupportedException(System.String message) : base(message) {
		
	}
	
	private const long serialVersionUID = 5876298277802827615L;
    }
}