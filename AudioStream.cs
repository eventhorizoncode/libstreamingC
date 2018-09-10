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
using Java.IO;

namespace Net.Majorkernelpanic.Streaming
{ 

    /** 
     * Don't use this class directly.
     */
    public abstract class AudioStream : MediaStream {

	    protected int mAudioSource;
	    protected int mOutputFormat;
	    protected int mAudioEncoder;
	    protected AudioQuality mRequestedQuality = AudioQuality.DEFAULT_AUDIO_QUALITY.clone();
        protected AudioQuality mQuality = AudioQuality.DEFAULT_AUDIO_QUALITY.clone();
	
	    public AudioStream() {
		    setAudioSource((int)AudioSource.Camcorder);
	    }
	
	    public void setAudioSource(int audioSource) {
		    mAudioSource = audioSource;
	    }

	    public void setAudioQuality(AudioQuality quality) {
		    mRequestedQuality = quality;
	    }
	
	    /** 
	     * Returns the quality of the stream.  
	     */
	    public AudioQuality getAudioQuality() {
		    return mQuality;
	    }	
	
	    protected void setAudioEncoder(int audioEncoder) {
		    mAudioEncoder = audioEncoder;
	    }
	
	    protected void setOutputFormat(int outputFormat) {
		    mOutputFormat = outputFormat;
	    }
	
	    
	    protected override void encodeWithMediaRecorder() {
		
		    // We need a local socket to forward data output by the camera to the packetizer
		    createSockets();

            Android.Util.Log.v(TAG,"Requested audio with "+mQuality.bitRate/1000+"kbps"+" at "+mQuality.samplingRate/1000+"kHz");
		
		    mMediaRecorder = new MediaRecorder();
		    mMediaRecorder.SetAudioSource((AudioSource)mAudioSource);
		    mMediaRecorder.SetOutputFormat((OutputFormat)mOutputFormat);
		    mMediaRecorder.SetAudioEncoder((AudioEncoder)mAudioEncoder);
		    mMediaRecorder.SetAudioChannels(1);
		    mMediaRecorder.SetAudioSamplingRate(mQuality.samplingRate);
		    mMediaRecorder.SetAudioEncodingBitRate(mQuality.bitRate);
		
		    // We write the output of the camera in a local socket instead of a file !			
		    // This one little trick makes streaming feasible quiet simply: data from the camera
		    // can then be manipulated at the other end of the socket
		    FileDescriptor fd = null;
		    if (sPipeApi == PIPE_API_PFD) {
			    fd = mParcelWrite.FileDescriptor;
		    } else  {
                fd = mSender.FileDescriptor;
		    }
		    mMediaRecorder.SetOutputFile(fd);
		    mMediaRecorder.SetOutputFile(fd);

		    mMediaRecorder.Prepare();
		    mMediaRecorder.Start();

		    InputStream iStream = null;
		
		    if (sPipeApi == PIPE_API_PFD) {
                iStream = new ParcelFileDescriptor.AutoCloseInputStream(mParcelRead);
		    } else  {
			    try {
                    // mReceiver.getInputStream contains the data from the camera
                    iStream = mReceiver.InputStream;
			    } catch (IOException e) {
				    stop();
				    throw new IOException("Something happened with the local sockets :/ Start failed !");
			    }
		    }

		    // the mPacketizer encapsulates this stream in an RTP stream and send it over the network
		    mPacketizer.setInputStream(iStream);
		    mPacketizer.start();
		    mStreaming = true;
		
	    }
	
    }
}
