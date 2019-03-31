using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;

namespace UpnpBrowser
{
	public partial class BrowserForm : Form
	{
		private List<UpnpRootDevice> devices = new List<UpnpRootDevice>();

		public BrowserForm()
		{
			InitializeComponent();

			Search();
		}

		private void Search()
		{
			var client = new UdpClient(1900, AddressFamily.InterNetwork);

			client.EnableBroadcast = true;
			client.AllowNatTraversal(true);
			client.JoinMulticastGroup(new IPAddress(new byte[] { 239, 255, 255, 250 }), 5);

			var request = @"M-SEARCH * HTTP/1.1
HOST: 239.255.255.250:1900
ST: ssdp:all
MAN: ""ssdp:discover""
MX: 3

";
//			var request = @"M-SEARCH * HTTP/1.1
//HOST: 239.255.255.250:1900
//ST: urn:schemas-upnp-org:service:ContentDirectory:1
//MAN: ""ssdp:discover""
//MX: 3

//";
			var data = Encoding.UTF8.GetBytes(request);

			client.Send(data, data.Length, new IPEndPoint(new IPAddress(new byte[] { 239, 255, 255, 250 }), 1900));

			client.BeginReceive(Client_Receive, client);
		}

		private void Client_Receive(IAsyncResult result)
		{
			var client = (UdpClient)result.AsyncState;

			IPEndPoint remoteEndPoint = null;

			var data = client.EndReceive(result, ref remoteEndPoint);

			if (!devices.Any(x => x.EndPoint.ToString() == remoteEndPoint.ToString()))
			{
				var stream = new MemoryStream(data);
				var reader = new StreamReader(stream);

				var request = reader.ReadLine();

				var headers = new List<Tuple<string, string>>();

				var line = reader.ReadLine();

				while (line != string.Empty)
				{
					var position = line.IndexOf(':');

					if(position == -1)
						headers.Add(new Tuple<string, string>(line, string.Empty));
					else
						headers.Add(new Tuple<string, string>(line.Substring(0, position), line.Substring(position + 1).Trim()));

					line = reader.ReadLine();
				}

				var type = request.Substring(0, request.IndexOf(' '));

				if (string.Equals(type, "NOTIFY", StringComparison.InvariantCultureIgnoreCase))
				{
					var device = new UpnpRootDevice { EndPoint = remoteEndPoint, Location = headers.Where(x => string.Equals(x.Item1, "Location", StringComparison.InvariantCultureIgnoreCase)).Select(x => x.Item2).FirstOrDefault() };

					devices.Add(device);

					DeviceFound(device);
				}
			}

			client.BeginReceive(Client_Receive, client);
		}

		private void DeviceFound(UpnpRootDevice device)
		{
			if (treeView.InvokeRequired)
			{
				treeView.Invoke((Action<UpnpRootDevice>)DeviceFound, device);
			}
			else
			{
				var node = new TreeNode
				{
					Text = device.Location,
					Tag = device
				};

				node.Nodes.Add("Loading...");

				treeView.Nodes.Add(node);
			}
		}

		private void treeView_AfterExpand(object sender, TreeViewEventArgs e)
		{
			Update();

			if (e.Node.Tag is UpnpRootDevice)
				Load((UpnpRootDevice)e.Node.Tag, e.Node);
			else if (e.Node.Tag is UpnpDevice)
				Load((UpnpDevice)e.Node.Tag, e.Node);
			else if (e.Node.Tag is UpnpDeviceService)
				Load((UpnpDeviceService)e.Node.Tag, e.Node);
			else if (e.Node.Tag is UpnpServiceAction)
				Load((UpnpServiceAction)e.Node.Tag, e.Node);
			else if (e.Node.Tag is ContentDirectoryContainer)
				Load((ContentDirectoryContainer)e.Node.Tag, e.Node);
		}

		private new void Load(UpnpRootDevice rootDevice, TreeNode node)
		{
			var document = XDocument.Load(rootDevice.Location);

			node.Nodes.Clear();

			var deviceElement = document.Element("{urn:schemas-upnp-org:device-1-0}root").Element("{urn:schemas-upnp-org:device-1-0}device");

			var device = new UpnpDevice
			{
				RootDevice = rootDevice,
				DeviceType = deviceElement.Element("{urn:schemas-upnp-org:device-1-0}deviceType").Value,
				PresentationUrl = deviceElement.Elements("{urn:schemas-upnp-org:device-1-0}presentationURL").Select(x => x.Value).FirstOrDefault(),
				FriendlyName = deviceElement.Element("{urn:schemas-upnp-org:device-1-0}friendlyName").Value,
				Manufacturer = deviceElement.Element("{urn:schemas-upnp-org:device-1-0}manufacturer").Value,
				ModelName = deviceElement.Element("{urn:schemas-upnp-org:device-1-0}modelName").Value,
				ModelNumber = deviceElement.Element("{urn:schemas-upnp-org:device-1-0}modelNumber").Value,
				SerialNumber = deviceElement.Element("{urn:schemas-upnp-org:device-1-0}serialNumber").Value,
				Udn = deviceElement.Element("{urn:schemas-upnp-org:device-1-0}UDN").Value,
				Services = new List<UpnpDeviceService>()
			};

			foreach (var serviceElement in deviceElement.Element("{urn:schemas-upnp-org:device-1-0}serviceList").Elements("{urn:schemas-upnp-org:device-1-0}service"))
			{
				UpnpDeviceService service = new UpnpDeviceService
				{
					Device = device,
					ServiceType = serviceElement.Element("{urn:schemas-upnp-org:device-1-0}serviceType").Value,
					ServiceID = serviceElement.Element("{urn:schemas-upnp-org:device-1-0}serviceId").Value,
					ServiceUrl = serviceElement.Element("{urn:schemas-upnp-org:device-1-0}SCPDURL").Value,
					ControlUrl = serviceElement.Element("{urn:schemas-upnp-org:device-1-0}controlURL").Value,
					EventSubscriptionUrl = serviceElement.Element("{urn:schemas-upnp-org:device-1-0}eventSubURL").Value,
				};

				device.Services.Add(service);

				if (string.Equals(service.ServiceID, "urn:upnp-org:serviceId:ContentDirectory"))
				{
					var serviceNode = new TreeNode
					{
						Text = device.FriendlyName,
						Tag = service
					};

					serviceNode.Nodes.Add("Loading...");

					node.Nodes.Add(serviceNode);
				}
			}

			//var deviceNode = new TreeNode
			//{
			//	Text = device.FriendlyName + " (" + device.ModelName + ")",
			//	Tag = device
			//};

			//deviceNode.Nodes.Add("Loading...");

			//node.Nodes.Clear();

			//node.Nodes.Add(deviceNode);
		}

		private new void Load(UpnpDevice device, TreeNode node)
		{
			node.Nodes.Clear();

			foreach (var service in device.Services)
			{
				var serviceNode = new TreeNode
				{
					Text = service.ServiceID + " (" + service.ServiceType + ")",
					Tag = service
				};

				serviceNode.Nodes.Add("Loading...");

				node.Nodes.Add(serviceNode);
			}
		}

		private new void Load(UpnpDeviceService deviceService, TreeNode node)
		{
			var client = new HttpClient();

			if (Uri.TryCreate(new Uri(deviceService.Device.RootDevice.Location), deviceService.ControlUrl, out Uri url))
			{
				var body = string.Format(@"<?xml version=""1.0""?>
<s:Envelope
	xmlns:s=""http://schemas.xmlsoap.org/soap/envelope/""
	s:encodingStyle=""http://schemas.xmlsoap.org/soap/encoding/"">
		<s:Body>
			<u:{0} xmlns:u=""{1}"">
				<ObjectID>0</ObjectID>
				<BrowseFlag>BrowseDirectChildren</BrowseFlag>
				<Filter>*</Filter>
				<StartingIndex>0</StartingIndex>
				<RequestedCount>0</RequestedCount>
				<SortCriteria></SortCriteria>
			</u:{0}>
		</s:Body>
</s:Envelope>", "Browse", deviceService.ServiceType);

				var request = new StringContent(body, Encoding.UTF8, "text/xml");

				request.Headers.Add("SOAPACTION", "\"" + deviceService.ServiceType + "#Browse\"");

				var response = client.PostAsync(url, request).Result;

				var data = response.Content.ReadAsByteArrayAsync().Result;

				var result = Encoding.UTF8.GetString(data);

				var document = XDocument.Parse(result);

				var envelopeElement = document.Element("{http://schemas.xmlsoap.org/soap/envelope/}Envelope");

				var responseElement = envelopeElement.Element("{http://schemas.xmlsoap.org/soap/envelope/}Body").Element("{urn:schemas-upnp-org:service:ContentDirectory:1}BrowseResponse");

				var resultElement = XElement.Parse(responseElement.Element("Result").Value);

				var numberReturned = responseElement.Element("NumberReturned").Value;

				node.Nodes.Clear();

				foreach (var containerElement in resultElement.Elements("{urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/}container"))
				{
					var container = new ContentDirectoryContainer
					{
						DeviceService = deviceService,
						ID = containerElement.Attribute("id").Value,
						ParentID = containerElement.Attribute("parentID").Value,
						Restricted = containerElement.Attribute("restricted").Value,
						ChildCount = containerElement.Attribute("childCount").Value,
						Searchable = containerElement.Attribute("searchable").Value,
						Title = containerElement.Element("{http://purl.org/dc/elements/1.1/}title").Value,
						ModificationTime = containerElement.Element("{http://www.pv.com/pvns/}modificationTime").Value,
						@Class = containerElement.Element("{urn:schemas-upnp-org:metadata-1-0/upnp/}class").Value,
					};

					var containerNode = new TreeNode
					{
						Text = container.Title,
						Tag = container
					};

					containerNode.Nodes.Add("Loading...");

					node.Nodes.Add(containerNode);
				}
			}
			else
				node.Nodes.Clear();
		}

		private new void Load(ContentDirectoryContainer container, TreeNode node)
		{
			var client = new HttpClient();

			if (Uri.TryCreate(new Uri(container.DeviceService.Device.RootDevice.Location), container.DeviceService.ControlUrl, out Uri url))
			{
				var body = string.Format(@"<?xml version=""1.0""?>
<s:Envelope
	xmlns:s=""http://schemas.xmlsoap.org/soap/envelope/""
	s:encodingStyle=""http://schemas.xmlsoap.org/soap/encoding/"">
		<s:Body>
			<u:{0} xmlns:u=""{1}"">
				<ObjectID>{2}</ObjectID>
				<BrowseFlag>BrowseDirectChildren</BrowseFlag>
				<Filter>*</Filter>
				<StartingIndex>0</StartingIndex>
				<RequestedCount>0</RequestedCount>
				<SortCriteria></SortCriteria>
			</u:{0}>
		</s:Body>
</s:Envelope>", "Browse", container.DeviceService.ServiceType, container.ID);

				var request = new StringContent(body, Encoding.UTF8, "text/xml");

				request.Headers.Add("SOAPACTION", "\"" + container.DeviceService.ServiceType + "#Browse\"");

				var response = client.PostAsync(url, request).Result;

				var data = response.Content.ReadAsByteArrayAsync().Result;

				var result = Encoding.UTF8.GetString(data);

				var document = XDocument.Parse(result);

				var envelopeElement = document.Element("{http://schemas.xmlsoap.org/soap/envelope/}Envelope");

				var responseElement = envelopeElement.Element("{http://schemas.xmlsoap.org/soap/envelope/}Body").Element("{urn:schemas-upnp-org:service:ContentDirectory:1}BrowseResponse");

				var resultElement = XElement.Parse(responseElement.Element("Result").Value);

				var numberReturned = responseElement.Element("NumberReturned").Value;

				node.Nodes.Clear();

				foreach (var containerElement in resultElement.Elements("{urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/}container"))
				{
					var childContainer = new ContentDirectoryContainer
					{
						DeviceService = container.DeviceService,
						ID = containerElement.Attribute("id").Value,
						ParentID = containerElement.Attribute("parentID").Value,
						Restricted = containerElement.Attribute("restricted").Value,
						ChildCount = containerElement.Attribute("childCount").Value,
						Searchable = containerElement.Attribute("searchable").Value,
						Title = containerElement.Element("{http://purl.org/dc/elements/1.1/}title").Value,
						ModificationTime = containerElement.Element("{http://www.pv.com/pvns/}modificationTime").Value,
						@Class = containerElement.Element("{urn:schemas-upnp-org:metadata-1-0/upnp/}class").Value,
					};

					var containerNode = new TreeNode
					{
						Text = childContainer.Title,
						Tag = childContainer
					};

					containerNode.Nodes.Add("Loading...");

					node.Nodes.Add(containerNode);
				}

				foreach (var itemElement in resultElement.Elements("{urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/}item"))
				{
					var resourceElement = itemElement.Element("{urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/}res");

					var item = new ContentDirectoryItem
					{
						DeviceService = container.DeviceService,
						ID = itemElement.Attribute("id").Value,
						ReferenceID = itemElement.Attribute("refID").Value,
						ParentID = itemElement.Attribute("parentID").Value,
						Restricted = itemElement.Attribute("restricted").Value,
						Title = itemElement.Element("{http://purl.org/dc/elements/1.1/}title").Value,
						Date = itemElement.Element("{http://purl.org/dc/elements/1.1/}date").Value,
						Genre = itemElement.Element("{urn:schemas-upnp-org:metadata-1-0/upnp/}genre").Value,
						Album = itemElement.Element("{urn:schemas-upnp-org:metadata-1-0/upnp/}album").Value,
						AlbumArtUri = itemElement.Element("{urn:schemas-upnp-org:metadata-1-0/upnp/}albumArtURI").Value,
						Extension = itemElement.Element("{http://www.pv.com/pvns/}extension").Value,
						ModificationTime = itemElement.Element("{http://www.pv.com/pvns/}modificationTime").Value,
						AddedTime = itemElement.Element("{http://www.pv.com/pvns/}addedTime").Value,
						LastUpdated = itemElement.Element("{http://www.pv.com/pvns/}lastUpdated").Value,
						Duration = resourceElement.Attributes("duration").Select(x => x.Value).FirstOrDefault(),
						Size = resourceElement.Attribute("size").Value,
						Resolution = resourceElement.Attributes("resolution").Select(x => x.Value).FirstOrDefault(),
						Bitrate = resourceElement.Attributes("bitrate").Select(x => x.Value).FirstOrDefault(),
						ProtocolInfo = resourceElement.Attribute("protocolInfo").Value,
						Location = resourceElement.Value,
						@Class = itemElement.Element("{urn:schemas-upnp-org:metadata-1-0/upnp/}class").Value,
					};

					var itemNode = new TreeNode
					{
						Text = item.Title,
						Tag = item
					};

					node.Nodes.Add(itemNode);
				}
			}
			else
				node.Nodes.Clear();
		}

		private new void Load(UpnpServiceAction action, TreeNode node)
		{
			var client = new HttpClient();

			if (Uri.TryCreate(new Uri(action.DeviceService.Device.RootDevice.Location), action.DeviceService.ControlUrl, out Uri url))
			{
//				var body = string.Format(@"<?xml version=""1.0""?>
//<s:Envelope
//	xmlns:s=""http://schemas.xmlsoap.org/soap/envelope/""
//	s:encodingStyle=""http://schemas.xmlsoap.org/soap/encoding/"">
//		<s:Body>
//			<u:{0} xmlns:u=""{1}""/>
//		</s:Body>
//</s:Envelope>", action.Name, action.DeviceService.ServiceType);

				var body = string.Format(@"<?xml version=""1.0""?>
<s:Envelope
	xmlns:s=""http://schemas.xmlsoap.org/soap/envelope/""
	s:encodingStyle=""http://schemas.xmlsoap.org/soap/encoding/"">
		<s:Body>
			<u:{0} xmlns:u=""{1}"">
				<ObjectID>0</ObjectID>
				<BrowseFlag>BrowseDirectChildren</BrowseFlag>
				<Filter>*</Filter>
				<StartingIndex>0</StartingIndex>
				<RequestedCount>0</RequestedCount>
				<SortCriteria></SortCriteria>
			</u:{0}>
		</s:Body>
</s:Envelope>", action.Name, action.DeviceService.ServiceType);

				var request = new StringContent(body, Encoding.UTF8, "text/xml");

				request.Headers.Add("SOAPACTION", "\"" + action.DeviceService.ServiceType + "#" + action.Name + "\"");

				var response = client.PostAsync(url, request).Result;

				var data = response.Content.ReadAsByteArrayAsync().Result;

				var result = Encoding.UTF8.GetString(data);
			}
		}

		private void treeView_AfterSelect(object sender, TreeViewEventArgs e)
		{
			if (e.Node.Tag is null)
				propertyGrid.SelectedObject = null;
			else
				propertyGrid.SelectedObject = e.Node.Tag;
		}

		private void treeView_NodeMouseDoubleClick(object sender, TreeNodeMouseClickEventArgs e)
		{
			if (e.Node.Tag is ContentDirectoryItem)
			{
				var item = (ContentDirectoryItem)e.Node.Tag;

				// Stream file from server
				var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Windows Media Player\\wmplayer.exe");

				System.Diagnostics.Process.Start(path, ((ContentDirectoryItem)e.Node.Tag).Location);



				// Download and play locally

				//var client = new HttpClient();

				//var stream = client.GetStreamAsync(((ContentDirectoryItem)e.Node.Tag).Location).Result;

				//var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ((ContentDirectoryItem)e.Node.Tag).Title + "." + ((ContentDirectoryItem)e.Node.Tag).Extension);
				//var file = File.Create(path);

				//stream.CopyTo(file);

				//file.Flush();
				//file.Close();

				//var path2 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Windows Media Player\\wmplayer.exe");
				//System.Diagnostics.Process.Start(path2, "\"" + path + "\"");



				// Download in 1 Mib chunks and play locally

				//var client = new HttpClient();
				//var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ((ContentDirectoryItem)e.Node.Tag).Title + "." + ((ContentDirectoryItem)e.Node.Tag).Extension);
				//var file = File.Create(path);

				//var position = 0;
				//var size = int.Parse(item.Size);

				//while (position < size)
				//{
				//	var request = new HttpRequestMessage(HttpMethod.Get, ((ContentDirectoryItem)e.Node.Tag).Location);

				//	request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(position, position + 1048576);

				//	var response = client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).Result;

				//	var stream = response.Content.ReadAsStreamAsync().Result;

				//	stream.CopyTo(file);

				//	position += (int)response.Content.Headers.ContentLength.Value;
				//}

				//file.Flush();
				//file.Close();

				//var path2 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Windows Media Player\\wmplayer.exe");

				//System.Diagnostics.Process.Start(path2, "\"" + path + "\"");
			}
		}
	}

	public class UpnpRootDevice
	{
		public IPEndPoint EndPoint { get; set; }
		public string Location { get; set; }
	}

	public class UpnpDevice
	{
		public UpnpRootDevice RootDevice { get; set; }
		public string DeviceType { get; set; }
		public string PresentationUrl { get; set; }
		public string FriendlyName { get; set; }
		public string Manufacturer { get; set; }
		public string ModelName { get; set; }
		public string ModelNumber { get; set; }
		public string SerialNumber { get; set; }
		public string Udn { get; set; }
		public List<UpnpDeviceService> Services { get; set; }
	}

	public class UpnpDeviceService
	{
		public UpnpDevice Device { get; set; }
		public string ServiceType { get; set; }
		public string ServiceID { get; set; }
		public string ServiceUrl { get; set; }
		public string ControlUrl { get; set; }
		public string EventSubscriptionUrl { get; set; }
	}

	public class UpnpServiceAction
	{
		public string Name { get; set; }
		public List<UpnpActionArgument> Arguments { get; set; }
		public UpnpDeviceService DeviceService { get; internal set; }
	}

	public class UpnpActionArgument
	{
		public string Name { get; set; }
		public string Direction { get; set; }
		public string RelatedStateVariable { get; set; }
	}

	public class UpnpServiceVariable
	{
		public string Name { get; set; }
		public string DataType { get; set; }
		public string SendEvents { get; set; }
		public UpnpDeviceService DeviceService { get; internal set; }
	}

	public class ContentDirectoryContainer
	{
		public string ID { get; set; }
		public string ParentID { get; set; }
		public string Restricted { get; set; }
		public string ChildCount { get; set; }
		public string Searchable { get; set; }
		public string Title { get; set; }
		public string ModificationTime { get; set; }
		public string Class { get; set; }
		public UpnpDeviceService DeviceService { get; internal set; }
	}

	public class ContentDirectoryItem
	{
		public UpnpDeviceService DeviceService { get; set; }
		public string ID { get; set; }
		public string ReferenceID { get; set; }
		public string ParentID { get; set; }
		public string Restricted { get; set; }
		public string Title { get; set; }
		public string Date { get; set; }
		public string Genre { get; set; }
		public string Album { get; set; }
		public string AlbumArtUri { get; set; }
		public string Extension { get; set; }
		public string ModificationTime { get; set; }
		public string AddedTime { get; set; }
		public string LastUpdated { get; set; }
		public string Duration { get; set; }
		public string Size { get; set; }
		public string Resolution { get; set; }
		public string Bitrate { get; set; }
		public string ProtocolInfo { get; set; }
		public string Location { get; set; }
		public string Class { get; set; }
	}
}
