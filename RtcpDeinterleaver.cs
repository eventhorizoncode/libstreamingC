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



using Java.IO;
using Java.Lang;

namespace  Net.Majorkernelpanic.Streaming.Rtsp
{

        class RtcpDeinterleaver :InputStream ,IRunnable {
	
	    public const String TAG = "RtcpDeinterleaver";
	
	    private IOException mIOException;
	    private InputStream mInputStream;
	    private PipedInputStream mPipedInputStream;
	    private PipedOutputStream mPipedOutputStream;
	    private byte[] mBuffer;
	
	    public RtcpDeinterleaver(InputStream inputStream) {
		    mInputStream = inputStream;
		    mPipedInputStream = new PipedInputStream(4096);
		    try {
			    mPipedOutputStream = new PipedOutputStream(mPipedInputStream);
		    } catch (IOException e) {}
		    mBuffer = new byte[1024];
		    new Thread(this).Start();
	    }

	    
	    public void Run() {
		    try {
			    while (true) {
				    int len = mInputStream.Read(mBuffer, 0, 1024);
				    mPipedOutputStream.Write(mBuffer, 0, len);
			    }
		    } catch (IOException e) {
                try { 
				    mPipedInputStream.Close ();
			    } catch (IOException ignore) {}
			    mIOException = e;
		    }
	    }

	    
	    public override int Read(byte[] buffer) {
		    if (mIOException != null) {
			    throw mIOException;
		    }
		    return mPipedInputStream.Read(buffer);
	    }		
	
	    
	    public override  int Read(byte[] buffer, int offset, int length) {
		    if (mIOException != null) {
			    throw mIOException;
		    }
		    return mPipedInputStream.Read(buffer, offset, length);
	    }	
	
	    public override int Read() {
		    if (mIOException != null) {
			    throw mIOException;
		    }
		    return mPipedInputStream.Read();
	    }

	    public override  void Close() {
		    mInputStream.Close();
	    }

    }
}