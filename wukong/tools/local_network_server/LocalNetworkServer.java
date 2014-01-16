import java.net.*; 
import java.io.*; 
import java.util.*;

public class LocalNetworkServer extends Thread
{
	protected static boolean serverContinue = true;
	protected static Map<Integer, LocalNetworkServer> clients;
	protected Socket clientSocket;
	protected int clientId;
	protected Queue<byte[]> messages;

	public static void main(String[] args) throws IOException 
	{ 
		clients = new HashMap<Integer, LocalNetworkServer>();

		ServerSocket serverSocket = null; 

		try { 
			serverSocket = new ServerSocket(10008); 
			System.out.println ("Connection Socket Created");
			try { 
				while (serverContinue) {
					serverSocket.setSoTimeout(10000);
					try {
						new LocalNetworkServer (serverSocket.accept()); 
					}
					catch (SocketTimeoutException ste) {
//						System.out.println ("Timeout Occurred");
					}
				}
			} 
			catch (IOException e) { 
				System.err.println("Accept failed."); 
				System.exit(1); 
			} 
		} 
		catch (IOException e) { 
			System.err.println("Could not listen on port: 10008."); 
			System.exit(1); 
		} 
		finally {
			try {
				System.out.println ("Closing Server Connection Socket");
				serverSocket.close(); 
			}
			catch (IOException e) {
				System.err.println("Could not close port: 10008."); 
				System.exit(1); 
			} 
		}
	}

	private LocalNetworkServer (Socket clientSoc)
	{
		messages = new LinkedList<byte[]>();
		clientSocket = clientSoc;
		start();
	}

	public void run()
	{
		try { 
			BufferedReader in = new BufferedReader(new InputStreamReader(clientSocket.getInputStream()));
			OutputStream out = clientSocket.getOutputStream();
			long lastHeartbeat = 0;

			// Say hi
			out.write(42);
			out.flush();

			// Get client id
			this.clientId = in.read();
			if (this.clientId < 0)
				throw new IOException("No ID received");
			this.clientId += 256*in.read();
			System.out.println("New client " + this.clientId);

			// Register this client in the global list
			LocalNetworkServer.clients.put(this.clientId, this);

			try {

				while(true) {
					// Receive messages
					if (in.ready()) {
						byte length = (byte)in.read();
						byte [] message = new byte[length+1];
						message[0] = (byte)length;
						for (int i=1; i<length; i++)
							message[i] = (byte)in.read();
						int destId = message[3] + message[4]*256;

						System.out.println("Received message from " + destId + ", length " + length);

						LocalNetworkServer destClient = LocalNetworkServer.clients.get(destId);
						if (destClient != null) {
							destClient.messages.add(message);
						}
						else
							System.out.println("Message for " + destId + " dropped.");
					}

					// Send messages
					if (this.messages.size() > 0) {
						System.out.println("Forwarding a message to node " + this.clientId);
						byte [] message = this.messages.poll();
						for (int i=0; i<message[0]; i++)
							out.write(message[i]);
						out.flush();
					}

					// Heartbeat
					if (System.currentTimeMillis() - lastHeartbeat > 1000) {
						lastHeartbeat = System.currentTimeMillis();
						out.write(0);
						out.flush();
					}

					Thread.yield();
				}
			}
			catch (IOException e) {
				System.out.println("Node " + this.clientId + " disconnected."); 
				LocalNetworkServer.clients.remove(this.clientId);
			}
		} 
		catch (IOException e) { 
			System.err.println("Problem with Communication Server");
			LocalNetworkServer.clients.remove(this.clientId);
		} 
	}
} 
