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
using Java.IO;
using Java.Net;
using Java.Util.Concurrent;
using System;
using Java.Lang;
using Android.Util;
using System.Collections.Generic;
using Java.Util.Regex;
using Java.Util;
using Java.Security;
using Android.Runtime;
/**
* RFC 2326.
* A basic and asynchronous RTSP client.
* The original purpose of this class was to implement a small RTSP client compatible with Wowza.
* It implements Digest Access Authentication according to RFC 2069. 
*/
namespace Net.Majorkernelpanic.Streaming
{

    public class RtspClient : IRunnable {

	    public static  System.String TAG = "RtspClient";

	    /** Message sent when the connection to the RTSP server failed. */
	    public const int ERROR_CONNECTION_FAILED = 0x01;
	
	    /** Message sent when the credentials are wrong. */
	    public const int ERROR_WRONG_CREDENTIALS = 0x03;
	
	    /** Use this to use UDP for the transport protocol. */
	    public const int TRANSPORT_UDP = RtpSocket.TRANSPORT_UDP;
	
	    /** Use this to use TCP for the transport protocol. */
	    public const int TRANSPORT_TCP = RtpSocket.TRANSPORT_TCP;	
	
	    /** 
	     * Message sent when the connection with the RTSP server has been lost for 
	     * some reason (for example, the user is going under a bridge).
	     * When the connection with the server is lost, the client will automatically try to
	     * reconnect as long as {@link #stopStream()} is not called. 
	     **/
	    public const int ERROR_CONNECTION_LOST = 0x04;
	
	    /**
	     * Message sent when the connection with the RTSP server has been reestablished.
	     * When the connection with the server is lost, the client will automatically try to
	     * reconnect as long as {@link #stopStream()} is not called.
	     */
	    public const int MESSAGE_CONNECTION_RECOVERED = 0x05;
	
	    private const int STATE_STARTED = 0x00;
	    private const int STATE_STARTING = 0x01;
	    private const int STATE_STOPPING = 0x02;
	    private const  int STATE_STOPPED = 0x03;
	    private int mState = 0;

	    private class Parameters {
		    public System.String host; 
		    public System.String username;
		    public System.String password;
		    public System.String path;
		    public Session session;
		    public int port;
		    public int transport;
		
		    public Parameters clone() {
			    Parameters param1 = new Parameters();
                param1.host = host;
                param1.username = username;
                param1.password = password;
                param1.path = path;
                param1.session = session;
                param1.port = port;
                param1.transport = transport;
			    return param1;
		    }
	    }
	
	
	    private Parameters mTmpParameters;
	    private Parameters mParameters;

	    private int mCSeq;
	    private Socket mSocket;
	    private System.String mSessionID;
	    private System.String mAuthorization;
	    private BufferedReader mBufferedReader;
	    private OutputStream mOutputStream;
	    private Callback mCallback;
	    private Handler mMainHandler;
	    private Handler mHandler;

	    /**
	     * The callback interface you need to implement to know what's going on with the 
	     * RTSP server (for example your Wowza Media Server).
	     */
	    public interface Callback {
		    void onRtspUpdate(int message, System.Exception exception);
	    }

	    public RtspClient() {
		    mCSeq = 0;
		    mTmpParameters = new Parameters();
		    mTmpParameters.port = 1935;
		    mTmpParameters.path = "/";
		    mTmpParameters.transport = TRANSPORT_UDP;
		    mAuthorization = null;
		    mCallback = null;
		    mMainHandler = new Handler(Looper.MainLooper);
		    mState = STATE_STOPPED;

		    Semaphore signal = new Semaphore(0);
            HandlerThread handlerThread = new HandlerThread("Net.Majorkernelpanic.Streaming.RtspClient");
          /*  {
			    
			    void onLooperPrepared() {
				    mHandler = new Handler();
				    signal.Release();
			    }
		    }//.start();
            */
          
            handlerThread.Start();

		    mHandler = new Handler(handlerThread.Looper);

            mMainHandler = new Handler(Looper.MainLooper);
            //handlerThread.

            signal.AcquireUninterruptibly();



            mConnectionMonitor = new Runnable(() => {
                void Run()
                {
                    if (mState == STATE_STARTED)
                    {
                        try
                        {
                            // We poll the RTSP server with OPTION requests
                            sendRequestOption();
                            mHandler.PostDelayed(mConnectionMonitor, 6000);
                        }
                        catch (IOException e)
                        {
                            // Happens if the OPTION request fails
                            postMessage(ERROR_CONNECTION_LOST);
                            Log.Error(TAG, "Connection lost with the server...");
                            mParameters.session.stop();
                            mHandler.Post(mRetryConnection);
                        }
                    }
                }
            });


            mRetryConnection = new Runnable(()=> {
                void Run()
                {
                    if (mState == STATE_STARTED)
                    {
                        try
                        {
                            Log.Error(TAG, "Trying to reconnect...");
                            tryConnection();
                            try
                            {
                                mParameters.session.start();
                                mHandler.Post(mConnectionMonitor);
                                postMessage(MESSAGE_CONNECTION_RECOVERED);
                            }
                            catch (System.Exception e)
                            {
                                abort();
                            }
                        }
                        catch (IOException e)
                        {
                            mHandler.PostDelayed(mRetryConnection, 1000);
                        }
                    }
                }
            });

    }

	    /**
	     * Sets the callback interface that will be called on status updates of the connection
	     * with the RTSP server.
	     * @param cb The implementation of the {@link Callback} interface
	     */
	    public void setCallback(Callback cb) {
		    mCallback = cb;
	    }

	    /**
	     * The {@link Session} that will be used to stream to the server.
	     * If not called before {@link #startStream()}, a it will be created.
	     */
	    public void setSession(Session session) {
		    mTmpParameters.session = session;
	    }

	    public Session getSession() {
		    return mTmpParameters.session;
	    }	

	    /**
	     * Sets the destination address of the RTSP server.
	     * @param host The destination address
	     * @param port The destination port
	     */
	    public void setServerAddress(System.String host, int port) {
		    mTmpParameters.port = port;
		    mTmpParameters.host = host;
	    }

	    /**
	     * If authentication is enabled on the server, you need to call this with a valid login/password pair.
	     * Only implements Digest Access Authentication according to RFC 2069.
	     * @param username The login
	     * @param password The password
	     */
	    public void setCredentials(System.String username, System.String password) {
		    mTmpParameters.username = username;
		    mTmpParameters.password = password;
	    }

	    /**
	     * The path to which the stream will be sent to. 
	     * @param path The path
	     */
	    public void setStreamPath(System.String path) {
		    mTmpParameters.path = path;
	    }

	    /**
	     * Call this with {@link #TRANSPORT_TCP} or {@value #TRANSPORT_UDP} to choose the 
	     * transport protocol that will be used to send RTP/RTCP packets.
	     * Not ready yet !
	     */
	    public void setTransportMode(int mode) {
		    mTmpParameters.transport = mode;
	    }
	
	    public bool isStreaming() {
		    return mState==STATE_STARTED||mState==STATE_STARTING;
	    }


        public void Run()
        {
            if (mState != STATE_STOPPED) return;
            mState = STATE_STARTING;

            Log.Debug(TAG, "Connecting to RTSP server...");

            // If the user calls some methods to configure the client, it won't modify its behavior until the stream is restarted
            mParameters = mTmpParameters.clone();
            mParameters.session.setDestination(mTmpParameters.host);

            try
            {
                mParameters.session.syncConfigure();
            }
            catch (System.Exception e)
            {
                mParameters.session = null;
                mState = STATE_STOPPED;
                return;
            }

            try
            {
                tryConnection();
            }
            catch (Java.Lang.Exception e)
            {
                postError(ERROR_CONNECTION_FAILED, e);
                abort();
                return;
            }

            try
            {
                mParameters.session.syncStart();
                mState = STATE_STARTED;
                if (mParameters.transport == TRANSPORT_UDP)
                {
                    mHandler.Post(mConnectionMonitor);
                }
            }
            catch (Java.Lang.Exception e)
            {
                abort();
            }



        }
        /**
	     * Connects to the RTSP server to publish the stream, and the effectively starts streaming.
	     * You need to call {@link #setServerAddress(String, int)} and optionally {@link #setSession(Session)} 
	     * and {@link #setCredentials(String, String)} before calling this.
	     * Should be called of the main thread !
	     */
        public void startStream() {
            if (mTmpParameters.host == null)
                   throw new IllegalStateException("setServerAddress(String,int) has not been called !");
            if (mTmpParameters.session == null)
                throw new IllegalStateException("setSession() has not been called !");

             mHandler.Post(this);

         }

	    /**
	     * Stops the stream, and informs the RTSP server.
	     */
	    public void stopStream() {
		    mHandler.Post(new Runnable (()=> {
			    void Run() {
				    if (mParameters != null && mParameters.session != null) {
					    mParameters.session.stop();
				    }
				    if (mState != STATE_STOPPED) {
					    mState = STATE_STOPPING;
					    abort();
				    }
			    }
		    }));
	    }

	    public void release() {
		    stopStream();
		    mHandler.Looper.Quit();
	    }
	
	    private void abort() {
		    try {
			    sendRequestTeardown();
		    } catch (System.Exception ignore) {}
		    try {
			    mSocket.Close();
		    } catch (Java.Lang.Exception ignore) {}
		    mHandler.RemoveCallbacks(mConnectionMonitor);
		    mHandler.RemoveCallbacks(mRetryConnection);
		    mState = STATE_STOPPED;
	    }
	
	    private void tryConnection() {
		    mCSeq = 0;
		    mSocket = new Socket(mParameters.host, mParameters.port);
		    mBufferedReader = new BufferedReader(new InputStreamReader(mSocket.InputStream));
		    mOutputStream = new BufferedOutputStream(mSocket.OutputStream);
		    sendRequestAnnounce();
		    sendRequestSetup();
		    sendRequestRecord();
	    }
	
	    /**
	     * Forges and sends the ANNOUNCE request 
	     */
	    private void sendRequestAnnounce() {

            System.String body = mParameters.session.getSessionDescription();
            System.String request = "ANNOUNCE rtsp://"+mParameters.host+":"+mParameters.port+mParameters.path+" RTSP/1.0\r\n" +
				    "CSeq: " + (++mCSeq) + "\r\n" +
				    "Content-Length: " + body.Length + "\r\n" +
				    "Content-Type: application/sdp\r\n\r\n" +
				    body;
		    Log.Info(TAG,request.Substring(0, request.IndexOf("\r\n")));

            mOutputStream.Write(System.Text.Encoding.UTF8.GetBytes(request));
		    mOutputStream.Flush();
		    Response response = Response.ParseResponse(mBufferedReader);

            System.String retval = "";
            if (response.headers.ContainsKey("server")) {
                response.headers.TryGetValue("server", out retval);

                Log.Verbose(TAG,"RTSP server name:" + retval);
		    } else {
			    Log.Verbose(TAG,"RTSP server name unknown");
		    }

		    if (response.headers.ContainsKey("session")) {
			    try {
                    response.headers.TryGetValue("session", out retval);

                    Matcher m = Response.rexegSession.Matcher(retval);
				    m.Find();
				    mSessionID = m.Group(1);
			    } catch (System.Exception e) {
				    throw new IOException("Invalid response from server. Session id: "+mSessionID);
			    }
		    }

		    if (response.status == 401) {
                System.String nonce, realm;
			    Matcher m;

			    if (mParameters.username == null || mParameters.password == null) throw new IllegalStateException("Authentication is enabled and setCredentials(String,String) was not called !");

			    try {
                    
                    response.headers.TryGetValue("www-authenticate", out retval);
                    m = Response.rexegAuthenticate.Matcher(retval);
                    m.Find();
				    nonce = m.Group(2);
				    realm = m.Group(1);
			    } catch (System.Exception e) {
				    throw new IOException("Invalid response from server");
			    }

                System.String uri = "rtsp://"+mParameters.host+":"+mParameters.port+mParameters.path;
                System.String s = mParameters.username + ":" + m.Group(1) + ":" + mParameters.password;
                System.String hash1 = computeMd5Hash((Java.Lang.String)s);
                s ="ANNOUNCE" + ":" + uri;
                System.String hash2 = computeMd5Hash((Java.Lang.String)s);
                s = hash1 + ":" + m.Group(2) + ":" + hash2;
                System.String hash3 = computeMd5Hash((Java.Lang.String)s);

			    mAuthorization = "Digest username=\""+mParameters.username+"\",realm=\""+realm+"\",nonce=\""+nonce+"\",uri=\""+uri+"\",response=\""+hash3+"\"";

			    request = "ANNOUNCE rtsp://"+mParameters.host+":"+mParameters.port+mParameters.path+" RTSP/1.0\r\n" +
					    "CSeq: " + (++mCSeq) + "\r\n" +
					    "Content-Length: " + body.Length + "\r\n" +
					    "Authorization: " + mAuthorization + "\r\n" +
					    "Session: " + mSessionID + "\r\n" +
					    "Content-Type: application/sdp\r\n\r\n" +
					    body;

			    Log.Info(TAG,request.Substring(0, request.IndexOf("\r\n")));

                System.Text.UTF8Encoding uEnc = new System.Text.UTF8Encoding();

                mOutputStream.Write(uEnc.GetBytes("UTF-8"));
			    mOutputStream.Flush();
			    response = Response.ParseResponse(mBufferedReader);

			    if (response.status == 401) throw new RuntimeException("Bad credentials !");

		    } else if (response.status == 403) {
			    throw new RuntimeException("Access forbidden !");
		    }

	    }

	    /**
	     * Forges and sends the SETUP request 
	     */
	    private void sendRequestSetup() {
		    for (int i=0;i<2;i++) {
			    Stream stream = mParameters.session.getTrack(i);
			    if (stream != null) {
                    System.String params1 = mParameters.transport==TRANSPORT_TCP ? 
						    "TCP;interleaved="+2*i+"-"+(2*i+1) : "UDP;unicast;client_port="+(5000+2*i)+"-"+(5000+2*i+1)+";mode=receive";
				    string request = "SETUP rtsp://"+mParameters.host+":"+mParameters.port+mParameters.path+"/trackID="+i+" RTSP/1.0\r\n" +
						    "Transport: RTP/AVP/"+ params1 + "\r\n" + addHeaders();

				    Log.Info(TAG,request.Substring(0, request.IndexOf("\r\n")));

				    mOutputStream.Write(System.Text.Encoding.UTF8.GetBytes(request));
				    mOutputStream.Flush();
				    Response response = Response.ParseResponse(mBufferedReader);
				    Matcher m;
				
				    if (response.headers.ContainsKey("session")) {
					    try {
                            string soutval;
                            response.headers.TryGetValue("session", out soutval);
                            m = Response.rexegSession.Matcher(soutval);
						    m.Find();
						    mSessionID = m.Group(1);
					    } catch (System.Exception e) { 
						    throw new IOException("Invalid response from server. Session id: "+mSessionID);
					    }
				    }
				
				    if (mParameters.transport == TRANSPORT_UDP) {
					    try {
                            string outval;
                            response.headers.TryGetValue("transport", out outval);

                            m = Response.rexegTransport.Matcher(outval); m.Find();
						    stream.setDestinationPorts(Integer.ParseInt(m.Group(3)), Integer.ParseInt(m.Group(4)));
						    Log.Debug(TAG, "Setting destination ports: "+Integer.ParseInt(m.Group(3))+", "+Integer.ParseInt(m.Group(4)));
					    } catch (System.Exception e) {
						    //e.StackTrace;
						    int[] ports = stream.getDestinationPorts();
						    Log.Debug(TAG,"Server did not specify ports, using default ports: "+ports[0]+"-"+ports[1]);
					    }
				    } else {
					    stream.setOutputStream(mOutputStream, (byte)(2*i));
				    }
			    }
		    }
	    }

	    /**
	     * Forges and sends the RECORD request 
	     */
	    private void sendRequestRecord() {
            string request = "RECORD rtsp://"+mParameters.host+":"+mParameters.port+mParameters.path+" RTSP/1.0\r\n" +
				    "Range: npt=0.000-\r\n" +
				    addHeaders();
		    Log.Info(TAG,request.Substring(0, request.IndexOf("\r\n")));
            

            mOutputStream.Write(System.Text.Encoding.UTF8.GetBytes(request));
		    mOutputStream.Flush();
		    Response.ParseResponse(mBufferedReader);
	    }

	    /**
	     * Forges and sends the TEARDOWN request 
	     */
	    private void sendRequestTeardown() {
            string request = "TEARDOWN rtsp://"+mParameters.host+":"+mParameters.port+mParameters.path+" RTSP/1.0\r\n" + addHeaders();
		    Log.Info(TAG,request.Substring(0, request.IndexOf("\r\n")));
		    mOutputStream.Write(System.Text.Encoding.UTF8.GetBytes(request));
		    mOutputStream.Flush();
	    }
	
	    /**
	     * Forges and sends the OPTIONS request 
	     */
	    private void sendRequestOption() {
            string request = "OPTIONS rtsp://"+mParameters.host+":"+mParameters.port+mParameters.path+" RTSP/1.0\r\n" + addHeaders();
		    Log.Info(TAG,request.Substring(0, request.IndexOf("\r\n")));
		    mOutputStream.Write(System.Text.Encoding.UTF8.GetBytes(request));
		    mOutputStream.Flush();
		    Response.ParseResponse(mBufferedReader);
	    }	

	    private System.String addHeaders() {
		    return "CSeq: " + (++mCSeq) + "\r\n" +
				    "Content-Length: 0\r\n" +
				    "Session: " + mSessionID + "\r\n" +
				    // For some reason you may have to remove last "\r\n" in the next line to make the RTSP client work with your wowza server :/
				    (mAuthorization != null ? "Authorization: " + mAuthorization + "\r\n":"") + "\r\n";
	    }

        /**
	     * If the connection with the RTSP server is lost, we try to reconnect to it as
	     * long as {@link #stopStream()} is not called.
	     */
        private Runnable mConnectionMonitor;

        /** Here, we try to reconnect to the RTSP. */
        private Runnable mRetryConnection;
	
	    protected static char[] hexArray = {'0','1','2','3','4','5','6','7','8','9','a','b','c','d','e','f'};

        IntPtr IJavaObject.Handle => throw new NotImplementedException();

        private static System.String bytesToHex(byte[] bytes) {
		    char[] hexChars = new char[bytes.Length * 2];
		    int v;
		    for ( int j = 0; j < bytes.Length; j++ ) {
			    v = bytes[j] & 0xFF;
			    hexChars[j * 2] = hexArray[v >> 4];
			    hexChars[j * 2 + 1] = hexArray[v & 0x0F];
		    }
		    return new System.String(hexChars);
	    }

	    /** Needed for the Digest Access Authentication. */
	    private System.String computeMd5Hash(Java.Lang.String buffer) {
		    MessageDigest md;
		    try {
			    md = MessageDigest.GetInstance("MD5");
			    return bytesToHex(md.Digest(buffer.GetBytes("UTF-8")));
		    } catch (NoSuchAlgorithmException ignore) {
		    } catch (UnsupportedEncodingException e) {}
		    return "";
	    }

	    private void postMessage(int message) {
		    mMainHandler.Post(new Runnable(() =>{
			    void Run() {
				    if (mCallback != null) {
					    mCallback.onRtspUpdate(message, null); 
				    }
			    }
		    }));
	    }

	    private void postError(int message, Java.Lang.Exception e) {
		    mMainHandler.Post(new Runnable(() =>{
			    
			    void Run() {
				    if (mCallback != null) {
					    mCallback.onRtspUpdate(message, e); 
				    }
			    }
		    }));
	    }

        void IRunnable.Run()
        {
            throw new NotImplementedException();
        }

        void IDisposable.Dispose()
        {
            throw new NotImplementedException();
        }
    }

	class Response {

		// Parses method & uri
		public static Java.Util.Regex.Pattern regexStatus = Java.Util.Regex.Pattern.Compile("RTSP/\\d.\\d (\\d+) (\\w+)", Java.Util.Regex.RegexOptions.CaseInsensitive);
		// Parses a request header
		public static Java.Util.Regex.Pattern rexegHeader = Java.Util.Regex.Pattern.Compile("(\\S+):(.+)", Java.Util.Regex.RegexOptions.CaseInsensitive);
		// Parses a WWW-Authenticate header
		public static Java.Util.Regex.Pattern rexegAuthenticate = Java.Util.Regex.Pattern.Compile("realm=\"(.+)\",\\s+nonce=\"(\\w+)\"", Java.Util.Regex.RegexOptions.CaseInsensitive);
		// Parses a Session header
		public static Java.Util.Regex.Pattern rexegSession = Java.Util.Regex.Pattern.Compile("(\\d+)", Java.Util.Regex.RegexOptions.CaseInsensitive);
		// Parses a Transport header
		public static Java.Util.Regex.Pattern rexegTransport = Java.Util.Regex.Pattern.Compile("client_port=(\\d+)-(\\d+).+server_port=(\\d+)-(\\d+)", Java.Util.Regex.RegexOptions.CaseInsensitive);


		public int status;

		public Dictionary<System.String, System.String> headers = new Dictionary<System.String, System.String>();

		/** Parse the method, URI & headers of a RTSP request */
		public static Response ParseResponse(BufferedReader input) {
			Response response = new Response();
			System.String line;
			Matcher matcher;
			// Parsing request method & URI
			if ((line = input.ReadLine())==null) throw new SocketException("Connection lost");
			matcher = regexStatus.Matcher(line);
			matcher.Find();
			response.status = Integer.ParseInt(matcher.Group(1));

			// Parsing headers of the request
			while ( (line = input.ReadLine()) != null) {
				//Log.e(TAG,"l: "+line.length()+", c: "+line);
				if (line.Length>3) {
					matcher = rexegHeader.Matcher(line);
					matcher.Find();
					response.headers.Add(matcher.Group(1).ToLower(System.Globalization.CultureInfo.CurrentCulture),matcher.Group(2));
				} else {
					break;
				}
			}
			if (line==null) throw new SocketException("Connection lost");

			Log.Debug(RtspClient.TAG, "Response from server: "+response.status);

			return response;
		}
	}

}