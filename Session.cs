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

using Android.OS;
using Java.Lang;
using Java.Net;
using Net.Majorkernelpanic.Streaming;
using System;
using System.IO;
using System.Text;

/**
 * You should instantiate this class with the {@link SessionBuilder}.<br />
 * This is the class you will want to use to stream audio and or video to some peer using RTP.<br />
 * 
 * It holds a {@link VideoStream} and a {@link AudioStream} together and provides 
 * synchronous and asynchronous functions to start and stop those steams.
 * You should implement a callback interface {@link Callback} to receive notifications and error reports.<br />
 * 
 * If you want to stream to a RTSP server, you will need an instance of this class and hand it to a {@link RtspClient}.
 * 
 * If you don't use the RTSP protocol, you will still need to send a session description to the receiver 
 * for him to be able to decode your audio/video streams. You can obtain this session description by calling 
 * {@link #configure()} or {@link #syncConfigure()} to configure the session with its parameters 
 * (audio samplingrate, video resolution) and then {@link Session#getSessionDescription()}.<br />
 * 
 * See the example 2 here: https://github.com/fyhertz/libstreaming-examples to 
 * see an example of how to get a SDP.<br />
 * 
 * See the example 3 here: https://github.com/fyhertz/libstreaming-examples to 
 * see an example of how to stream to a RTSP server.<br />
 * 
 */
namespace Net.Majorkernelpanic.Streaming
{

    public class Session
    {

        public const System.String TAG = "Session";

        public const int STREAM_VIDEO = 0x01;

        public const int STREAM_AUDIO = 0x00;

        /** Some app is already using a camera (Camera.open() has failed). */
        public const int ERROR_CAMERA_ALREADY_IN_USE = 0x00;

        /** The phone may not support some streaming parameters that you are trying to use (bit rate, frame rate, resolution...). */
        public const int ERROR_CONFIGURATION_NOT_SUPPORTED = 0x01;

        /** 
         * The internal storage of the phone is not ready. 
         * libstreaming tried to store a test file on the sdcard but couldn't.
         * See H264Stream and AACStream to find out why libstreaming would want to something like that. 
         */
        public const int ERROR_STORAGE_NOT_READY = 0x02;

        /** The phone has no flash. */
        public const int ERROR_CAMERA_HAS_NO_FLASH = 0x03;

        /** The supplied SurfaceView is not a valid surface, or has not been created yet. */
        public const int ERROR_INVALID_SURFACE = 0x04;

        /** 
         * The destination set with {@link Session#setDestination(String)} could not be resolved. 
         * May mean that the phone has no access to the internet, or that the DNS server could not
         * resolved the host name.
         */
        public const int ERROR_UNKNOWN_HOST = 0x05;

        /**
         * Some other error occurred !
         */
        public const int ERROR_OTHER = 0x06;

        private System.String mOrigin;
        private System.String mDestination;
        private int mTimeToLive = 64;
        private long mTimestamp;

        private AudioStream mAudioStream = null;
        private VideoStream mVideoStream = null;

        private Callback mCallback;
        private Handler mMainHandler;

        private Handler mHandler;

        /** 
         * Creates a streaming session that can be customized by adding tracks.
         */
        public Session() {
            long uptime = Java.Lang.JavaSystem.CurrentTimeMillis();

            HandlerThread thread = new HandlerThread("Net.Majorkernelpanic.Streaming.Session");
            thread.Start();

            mHandler = new Handler(thread.Looper);
            mMainHandler = new Handler(Looper.MainLooper);
            mTimestamp = (uptime / 1000) << 32 & (((uptime - ((uptime / 1000) * 1000)) >> 32) / 1000); // NTP timestamp
            mOrigin = "127.0.0.1";


            mUpdateBitrate = new Runnable(() => {

                void Run()
                {
                    if (isStreaming())
                    {
                        postBitRate(getBitrate());
                        mHandler.PostDelayed(mUpdateBitrate, 500);
                    }
                    else
                    {
                        postBitRate(0);
                    }
                }
            });
        }

        /**
         * The callback interface you need to implement to get some feedback
         * Those will be called from the UI thread.
         */
        public interface Callback {

            /** 
             * Called periodically to inform you on the bandwidth 
             * consumption of the streams when streaming. 
             */
            void onBitrateUpdate(long bitrate);

            /** Called when some error occurs. */
            void onSessionError(int reason, int streamType, System.Exception e);

            /** 
             * Called when the previw of the {@link VideoStream}
             * has correctly been started.
             * If an error occurs while starting the preview,
             * {@link Callback#onSessionError(int, int, Exception)} will be
             * called instead of {@link Callback#onPreviewStarted()}.
             */
            void onPreviewStarted();

            /** 
             * Called when the session has correctly been configured 
             * after calling {@link Session#configure()}.
             * If an error occurs while configuring the {@link Session},
             * {@link Callback#onSessionError(int, int, Exception)} will be
             * called instead of  {@link Callback#onSessionConfigured()}.
             */
            void onSessionConfigured();

            /** 
             * Called when the streams of the session have correctly been started.
             * If an error occurs while starting the {@link Session},
             * {@link Callback#onSessionError(int, int, Exception)} will be
             * called instead of  {@link Callback#onSessionStarted()}. 
             */
            void onSessionStarted();

            /** Called when the stream of the session have been stopped. */
            void onSessionStopped();

        }

        /** You probably don't need to use that directly, use the {@link SessionBuilder}. */
        void addAudioTrack(AudioStream track) {
            removeAudioTrack();
            mAudioStream = track;
        }

        /** You probably don't need to use that directly, use the {@link SessionBuilder}. */
        void addVideoTrack(VideoStream track) {
            removeVideoTrack();
            mVideoStream = track;
        }

        /** You probably don't need to use that directly, use the {@link SessionBuilder}. */
        void removeAudioTrack() {
            if (mAudioStream != null) {
                mAudioStream.stop();
                mAudioStream = null;
            }
        }

        /** You probably don't need to use that directly, use the {@link SessionBuilder}. */
        void removeVideoTrack() {
            if (mVideoStream != null) {
                mVideoStream.stopPreview();
                mVideoStream = null;
            }
        }

        /** Returns the underlying {@link AudioStream} used by the {@link Session}. */
        public AudioStream getAudioTrack() {
            return mAudioStream;
        }

        /** Returns the underlying {@link VideoStream} used by the {@link Session}. */
        public VideoStream getVideoTrack() {
            return mVideoStream;
        }

        /**
         * Sets the callback interface that will be called by the {@link Session}.
         * @param callback The implementation of the {@link Callback} interface
         */
        public void setCallback(Callback callback) {
            mCallback = callback;
        }

        /** 
         * The origin address of the session.
         * It appears in the session description.
         * @param origin The origin address
         */
        public void setOrigin(System.String origin) {
            mOrigin = origin;
        }

        /** 
         * The destination address for all the streams of the session. <br />
         * Changes will be taken into account the next time you start the session.
         * @param destination The destination address
         */
        public void setDestination(System.String destination) {
            mDestination = destination;
        }

        /** 
         * Set the TTL of all packets sent during the session. <br />
         * Changes will be taken into account the next time you start the session.
         * @param ttl The Time To Live
         */
        public void setTimeToLive(int ttl) {
            mTimeToLive = ttl;
        }

        /** 
         * Sets the configuration of the stream. <br />
         * You can call this method at any time and changes will take 
         * effect next time you call {@link #configure()}.
         * @param quality Quality of the stream
         */
        public void setVideoQuality(VideoQuality quality) {
            if (mVideoStream != null) {
                mVideoStream.setVideoQuality(quality);
            }
        }

        /**
         * Sets a Surface to show a preview of recorded media (video). <br />
         * You can call this method at any time and changes will take 
         * effect next time you call {@link #start()} or {@link #startPreview()}.
         */
        public void setSurfaceView(SurfaceView view)
        {
            mHandler.Post(new Runnable(() => {

                void Run() {
                    if (mVideoStream != null) {
                        mVideoStream.setSurfaceView(view);
                    }
                }
            }));
        }

        /** 
         * Sets the orientation of the preview. <br />
         * You can call this method at any time and changes will take 
         * effect next time you call {@link #configure()}.
         * @param orientation The orientation of the preview
         */
        public void setPreviewOrientation(int orientation) {
            if (mVideoStream != null) {
                mVideoStream.setPreviewOrientation(orientation);
            }
        }

        /** 
         * Sets the configuration of the stream. <br />
         * You can call this method at any time and changes will take 
         * effect next time you call {@link #configure()}.
         * @param quality Quality of the stream
         */
        public void setAudioQuality(AudioQuality quality) {
            if (mAudioStream != null) {
                mAudioStream.setAudioQuality(quality);
            }
        }

        /**
         * Returns the {@link Callback} interface that was set with 
         * {@link #setCallback(Callback)} or null if none was set.
         */
        public Callback getCallback() {
            return mCallback;
        }

        /** 
         * Returns a Session Description that can be stored in a file or sent to a client with RTSP.
         * @return The Session Description.
         * @throws IllegalStateException Thrown when {@link #setDestination(String)} has never been called.
         */
        public System.String getSessionDescription() {
            Java.Lang.StringBuilder sessionDescription = new Java.Lang.StringBuilder();
            if (mDestination == null) {
                throw new IllegalStateException("setDestination() has not been called !");
            }
            sessionDescription.Append("v=0\r\n");
            // TODO: Add IPV6 support
            sessionDescription.Append("o=- " + mTimestamp + " " + mTimestamp + " IN IP4 " + mOrigin + "\r\n");
            sessionDescription.Append("s=Unnamed\r\n");
            sessionDescription.Append("i=N/A\r\n");
            sessionDescription.Append("c=IN IP4 " + mDestination + "\r\n");
            // t=0 0 means the session is permanent (we don't know when it will stop)
            sessionDescription.Append("t=0 0\r\n");
            sessionDescription.Append("a=recvonly\r\n");
            // Prevents two different sessions from using the same peripheral at the same time
            if (mAudioStream != null) {
                sessionDescription.Append(mAudioStream.getSessionDescription());
                sessionDescription.Append("a=control:trackID=" + 0 + "\r\n");
            }
            if (mVideoStream != null) {
                sessionDescription.Append(mVideoStream.getSessionDescription());
                sessionDescription.Append("a=control:trackID=" + 1 + "\r\n");
            }
            return sessionDescription.ToString();
        }

        /** Returns the destination set with {@link #setDestination(String)}. */
        public System.String getDestination() {
            return mDestination;
        }

        /** Returns an approximation of the bandwidth consumed by the session in bit per second. */
        public long getBitrate() {
            long sum = 0;
            if (mAudioStream != null) sum += mAudioStream.getBitrate();
            if (mVideoStream != null) sum += mVideoStream.getBitrate();
            return sum;
        }

        /** Indicates if a track is currently running. */
        public bool isStreaming() {
            return (mAudioStream != null && mAudioStream.isStreaming()) || (mVideoStream != null && mVideoStream.isStreaming());
        }

        /** 
         * Configures all streams of the session.
         **/
        public void configure() {
            mHandler.Post(new Runnable(() => {

                void Run() {
                    try {
                        syncConfigure();
                    } catch (System.Exception e) { };
                }
            }));
        }


        /** 
         * Does the same thing as {@link #configure()}, but in a synchronous manner. <br />
         * Throws exceptions in addition to calling a callback 
         * {@link Callback#onSessionError(int, int, Exception)} when
         * an error occurs.	
         **/
        public void syncConfigure()
        {
            
                    for (int id=0;id<2;id++) {
                        Stream stream = id==0 ? (Stream)mAudioStream: (Stream)mVideoStream;
                        if (stream!=null && !stream.isStreaming()) {
                            try {
                                stream.configure();
                            } catch (CameraInUseException e) {
                                postError(ERROR_CAMERA_ALREADY_IN_USE , id, e);
                                throw e;
                            } catch (StorageUnavailableException e) {
                                postError(ERROR_STORAGE_NOT_READY , id, e);
                                throw e;
                            } catch (ConfNotSupportedException e) {
                                postError(ERROR_CONFIGURATION_NOT_SUPPORTED , id, e);
                                throw e;
                            } catch (InvalidSurfaceException e) {
                                postError(ERROR_INVALID_SURFACE , id, e);
                                throw e;
                            } catch (IOException e) {
                                postError(ERROR_OTHER, id, e);
                                throw e;
                            } catch (RuntimeException e) {
                                postError(ERROR_OTHER, id, e);
                                throw e;
                            }
                        }
                    }
                    postSessionConfigured();
        }

        /** 
         * Asynchronously starts all streams of the session.
         **/
        public void start() {
            mHandler.Post(new Runnable(() => {

                void run() {
                    try {
                        syncStart();
                    } catch (System.Exception e) { }
                }
            }));
        }

        /** 
         * Starts a stream in a synchronous manner. <br />
         * Throws exceptions in addition to calling a callback.
         * @param id The id of the stream to start
         **/
        public void syncStart(int id)
        {

            Stream stream = id == 0 ? (Stream)mAudioStream : mVideoStream;
            if (stream != null && !stream.isStreaming()) {
                try {
                    InetAddress destination = InetAddress.GetByName(mDestination);
                    stream.setTimeToLive(mTimeToLive);
                    stream.setDestinationAddress(destination);
                    stream.start();
                    if (getTrack(1 - id) == null || getTrack(1 - id).isStreaming()) {
                        postSessionStarted();
                    }
                    if (getTrack(1 - id) == null || !getTrack(1 - id).isStreaming()) {
                        mHandler.Post(mUpdateBitrate);
                    }
                } catch (UnknownHostException e) {
                    postError(ERROR_UNKNOWN_HOST, id, e);
                    throw e;
                } catch (CameraInUseException e) {
                    postError(ERROR_CAMERA_ALREADY_IN_USE, id, e);
                    throw e;
                } catch (StorageUnavailableException e) {
                    postError(ERROR_STORAGE_NOT_READY, id, e);
                    throw e;
                } catch (ConfNotSupportedException e) {
                    postError(ERROR_CONFIGURATION_NOT_SUPPORTED, id, e);
                    throw e;
                } catch (InvalidSurfaceException e) {
                    postError(ERROR_INVALID_SURFACE, id, e);
                    throw e;
                } catch (IOException e) {
                    postError(ERROR_OTHER, id, e);
                    throw e;
                } catch (RuntimeException e) {
                    postError(ERROR_OTHER, id, e);
                    throw e;
                }
            }

        }

        /** 
         * Does the same thing as {@link #start()}, but in a synchronous manner. <br /> 
         * Throws exceptions in addition to calling a callback.
         **/
        public void syncStart()
        {

            syncStart(1);
            try {
                syncStart(0);
            } catch (RuntimeException e) {
                syncStop(1);
                throw e;
            } catch (IOException e) {
                syncStop(1);
                throw e;
            }

        }

        /** Stops all existing streams. */
        public void stop() {
            mHandler.Post(new Runnable(() => {

                void Run() {
                    syncStop();
                }
            }));
        }

        /** 
         * Stops one stream in a synchronous manner.
         * @param id The id of the stream to stop
         **/
        private void syncStop(int id) {
            Stream stream = id == 0 ? (Stream)mAudioStream : (Stream)mVideoStream;
            if (stream != null) {
                stream.stop();
            }
        }

        /** Stops all existing streams in a synchronous manner. */
        public void syncStop() {
            syncStop(0);
            syncStop(1);
            postSessionStopped();
        }

        /**
         * Asynchronously starts the camera preview. <br />
         * You should of course pass a {@link SurfaceView} to {@link #setSurfaceView(SurfaceView)}
         * before calling this method. Otherwise, the {@link Callback#onSessionError(int, int, Exception)}
         * callback will be called with {@link #ERROR_INVALID_SURFACE}.
         */
        public void startPreview() {
            mHandler.Post(new Runnable(() => {

                void Run() {
                    if (mVideoStream != null) {
                        try {
                            mVideoStream.startPreview();
                            postPreviewStarted();
                            mVideoStream.configure();
                        } catch (CameraInUseException e) {
                            postError(ERROR_CAMERA_ALREADY_IN_USE, STREAM_VIDEO, e);
                        } catch (ConfNotSupportedException e) {
                            postError(ERROR_CONFIGURATION_NOT_SUPPORTED, STREAM_VIDEO, e);
                        } catch (InvalidSurfaceException e) {
                            postError(ERROR_INVALID_SURFACE, STREAM_VIDEO, e);
                        } catch (RuntimeException e) {
                            postError(ERROR_OTHER, STREAM_VIDEO, e);
                        } catch (StorageUnavailableException e) {
                            postError(ERROR_STORAGE_NOT_READY, STREAM_VIDEO, e);
                        } catch (IOException e) {
                            postError(ERROR_OTHER, STREAM_VIDEO, e);
                        }
                    }
                }
            }));
        }

        /**
         * Asynchronously stops the camera preview.
         */
        public void stopPreview() {
            mHandler.Post(new Runnable(() => {

                void Run() {
                    if (mVideoStream != null) {
                        mVideoStream.stopPreview();
                    }
                }
            });
        }

        /**	Switch between the front facing and the back facing camera of the phone. <br />
         * If {@link #startPreview()} has been called, the preview will be  briefly interrupted. <br />
         * If {@link #start()} has been called, the stream will be  briefly interrupted.<br />
         * To find out which camera is currently selected, use {@link #getCamera()}
         **/
        public void switchCamera() {
            mHandler.Post(new Runnable(() => {

                void Run() {
                    if (mVideoStream != null) {
                        try {
                            mVideoStream.switchCamera();
                            postPreviewStarted();
                        } catch (CameraInUseException e) {
                            postError(ERROR_CAMERA_ALREADY_IN_USE, STREAM_VIDEO, e);
                        } catch (ConfNotSupportedException e) {
                            postError(ERROR_CONFIGURATION_NOT_SUPPORTED, STREAM_VIDEO, e);
                        } catch (InvalidSurfaceException e) {
                            postError(ERROR_INVALID_SURFACE, STREAM_VIDEO, e);
                        } catch (IOException e) {
                            postError(ERROR_OTHER, STREAM_VIDEO, e);
                        } catch (RuntimeException e) {
                            postError(ERROR_OTHER, STREAM_VIDEO, e);
                        }
                    }
                }
            });
        }

        /**
         * Returns the id of the camera currently selected. <br />
         * It can be either {@link CameraInfo#CAMERA_FACING_BACK} or 
         * {@link CameraInfo#CAMERA_FACING_FRONT}.
         */
        public int getCamera() {
            return mVideoStream != null ? mVideoStream.getCamera() : 0;

        }

        /** 
         * Toggles the LED of the phone if it has one.
         * You can get the current state of the flash with 
         * {@link Session#getVideoTrack()} and {@link VideoStream#getFlashState()}.
         **/
        public void toggleFlash() {
            mHandler.Post(new Runnable(() => {

                void Run() {
                    if (mVideoStream != null) {
                        try {
                            mVideoStream.toggleFlash();
                        } catch (RuntimeException e) {
                            postError(ERROR_CAMERA_HAS_NO_FLASH, STREAM_VIDEO, e);
                        }
                    }
                }
            });
        }

        /** Deletes all existing tracks & release associated resources. */
        public void release() {
            removeAudioTrack();
            removeVideoTrack();
            mHandler.Looper.Quit()
    
    }

        private void postPreviewStarted() {
            mMainHandler.Post(new Runnable(() => {

                void Run() {
                    if (mCallback != null) {
                        mCallback.onPreviewStarted();
                    }
                }
            }));
        }

        private void postSessionConfigured() {
            mMainHandler.Post(new Runnable(() => {

                void Run() {
                    if (mCallback != null) {
                        mCallback.onSessionConfigured();
                    }
                }
            });
        }

        private void postSessionStarted() {
            mMainHandler.Post(new Runnable(() => {

                void Run() {
                    if (mCallback != null) {
                        mCallback.onSessionStarted();
                    }
                }
            }));
        }

        private void postSessionStopped() {
            mMainHandler.Post(new Runnable(() => {

                void Run() {
                    if (mCallback != null) {
                        mCallback.onSessionStopped();
                    }
                }
            }));
        }

        private void postError(int reason, int streamType, System.Exception e) {
            mMainHandler.Post(new Runnable(() => {

                void Run() {
                    if (mCallback != null) {
                        mCallback.onSessionError(reason, streamType, e);
                    }
                }
            }));
        }

        private void postBitRate(long bitrate) {
            mMainHandler.Post(new Runnable(() => {

                void Run() {
                    if (mCallback != null) {
                        mCallback.onBitrateUpdate(bitrate);
                    }
                }
            }));
        }

        public Runnable mUpdateBitrate;
      


        public bool trackExists(int id) {
            if (id == 0)
                return mAudioStream != null;
            else
                return mVideoStream != null;
        }

        public Stream getTrack(int id) {
            if (id == 0)
                return (Stream)mAudioStream;
            else
                return (Stream)mVideoStream;
        }

    }
}
