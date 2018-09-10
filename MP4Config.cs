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

/**
 * Finds SPS & PPS parameters in mp4 file.
 */
    public class MP4Config {

	    public const System.String TAG = "MP4Config";
	
	    private MP4Parser mp4Parser;
	    private System.String mProfilLevel, mPPS, mSPS;

	    public MP4Config(System.String profil, System.String sps, System.String pps) {
		    mProfilLevel = profil; 
		    mPPS = pps; 
		    mSPS = sps;
	    }

	    public MP4Config(System.String sps, System.String pps) {
		    mPPS = pps;
		    mSPS = sps;
		    mProfilLevel = MP4Parser.toHexString(Base64.Decode(sps, Base64Flags.NoWrap),1,3);
	    }	
	
	    public MP4Config(byte[] sps, byte[] pps) {
		    mPPS = Base64.EncodeToString(pps, 0, pps.Length, Base64Flags.NoWrap );
		    mSPS = Base64.EncodeToString(sps, 0, sps.Length, Base64Flags.NoWrap);
		    mProfilLevel = MP4Parser.toHexString(sps,1,3);
	    }
	
	    /**
	     * Finds SPS & PPS parameters inside a .mp4.
	     * @param path Path to the file to analyze
	     * @throws IOException
	     * @throws FileNotFoundException
	     */
	    public MP4Config (System.String path) {

		    StsdBox stsdBox; 
		
		    // We open the mp4 file and parse it
		    try {
			    mp4Parser = MP4Parser.parse(path);
		    } catch (IOException ignore) {
			    // Maybe enough of the file has been parsed and we can get the stsd box
		    }

		    // We find the stsdBox
		    stsdBox = mp4Parser.getStsdBox();
		    mPPS = stsdBox.getB64PPS();
		    mSPS = stsdBox.getB64SPS();
		    mProfilLevel = stsdBox.getProfileLevel();

		    mp4Parser.close();
		
	    }

	    public System.String getProfileLevel() {
		    return mProfilLevel;
	    }

	    public System.String getB64PPS() {
		    Log.Debug(TAG, "PPS: "+mPPS);
		    return mPPS;
	    }

	    public System.String getB64SPS() {
		    Log.Debug(TAG, "SPS: "+mSPS);
		    return mSPS;
	    }

    }
}