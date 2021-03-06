﻿#region Copyright
/* * * * * * * * * * * * * * * * * * * * * * * * * */
/* Carl Zeiss IMT (IZfM Dresden)                   */
/* Softwaresystem PiWeb                            */
/* (c) Carl Zeiss 2011                             */
/* * * * * * * * * * * * * * * * * * * * * * * * * */
#endregion

namespace Zeiss.IMT.PiWeb.MeshModel
{
	#region using

	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.IO.Compression;
	using System.Linq;
	using System.Xml;

	#endregion

	/// <summary>
	/// Describes the meta data of a <see cref="MeshModelPart"/> or <see cref="MeshModel"/>.
	/// </summary>
	public class MeshModelMetadata
	{
		#region constructor


		/// <summary>
		/// Initializes a new instance of the <see cref="MeshModelMetadata" /> class.
		/// </summary>
		/// <param name="fileVersion">The file version.</param>
		/// <param name="format">The source format.</param>
		/// <param name="name">The name.</param>
		/// <param name="layers">The layers to which this model or part belongs.</param>
		public MeshModelMetadata(Version fileVersion = null, string format = "", string name = "", string[] layers = null)
		{
			FileVersion = fileVersion ?? MeshModel.MeshModelFileVersion;
			SourceFormat = format;
			Name = name;
			Layer = layers ?? new string[0];
		}

		#endregion

		#region properties

		/// <summary>
		/// Gets or sets the file version.
		/// </summary>
		public Version FileVersion { get; set; }

		/// <summary>
		/// Gets or sets the format from which the model was initially created.
		/// </summary>
		public string SourceFormat { get; set; }
		
		/// <summary>
		/// Gets or sets the unique identifier. It is used to detect changed models on the PiWeb server.
		/// </summary>
		public Guid Guid { get; set; } = Guid.NewGuid();

		/// <summary>
		/// Gets or sets the triangulation hash. Models with the same triangulation hash are defined to have the same triangle mesh.
		/// </summary>
		public Guid TriangulationHash { get; internal set; }
		
		/// <summary>
		/// Gets or sets the name of the model.
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// Gets the number of parts in the model.
		/// </summary>
		public int PartCount { get; private set; } = 1;
		
		/// <summary>
		/// Gets or sets the layers that exist in the model.
		/// </summary>
		public string[] Layer { get; set; }

		/// <summary>
		/// Gets the names of the source models, in case this is a combined model
		/// </summary>
		public string[] SourceModels { get; private set; } = new string[0];

		/// <summary>
		/// Gets the mesh value entries that exist in the associated <see cref="MeshModelPart"/> or <see cref="MeshModel"/>.
		/// </summary>
		/// <value>
		/// The mesh value entries.
		/// </value>
		public MeshValueEntry[] MeshValueEntries { get; internal set; } = new MeshValueEntry[0];

		#endregion

		#region methods

		/// <summary>
		/// Combines the specified <paramref name="metadatas"/> and sets the name of the combined <see cref="MeshModelMetadata"/>.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <param name="metadatas">The metadatas.</param>
		/// <returns></returns>
		public static MeshModelMetadata CreateCombined(string name, params MeshModelMetadata[] metadatas)
		{
			var format = metadatas[0].SourceFormat;
			var layer = new HashSet<string>();
			var sourceModels = new List<string>();

			for (var i = 0; i < metadatas.Length; i += 1)
			{
				var metadata = metadatas[i];

				if (format != metadata.SourceFormat)
					format = "";

				layer.UnionWith(metadata.Layer);

				sourceModels.AddRange(metadata.SourceModels);
				if (!string.IsNullOrEmpty(metadata.Name) && !sourceModels.Contains(metadata.Name))
					sourceModels.Add(metadata.Name);

				if (string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(metadata.Name))
					name = metadata.Name;
			}

			return new MeshModelMetadata { Name = name, SourceFormat = format, Layer = layer.ToArray(), PartCount = metadatas.Length, SourceModels = sourceModels.ToArray() };
		}


		/// <summary>
		/// Extracts the <see cref="MeshModelMetadata"/> from the specified <paramref name="stream"/>.
		/// </summary>
		/// <remarks>
		/// The stream must contain a meshmodel file which has been created with the <see cref="MeshModel.Serialize(Stream)"/> method.
		/// </remarks>
		public static MeshModelMetadata ExtractFromStream( Stream stream )
		{
			if( stream == null )
				throw new ArgumentNullException( nameof(stream) );

			using( var zipFile = new ZipArchive( stream.CanSeek ? stream : new MemoryStream( MeshModelHelper.StreamToArray( stream ) ), ZipArchiveMode.Read ) )
			using( var entryStream = zipFile.GetEntry( "Metadata.xml" ).Open() )
			{
				var result = ReadFrom( entryStream );

				// falls Guid noch nicht Teil der Metadaten war, dann hier stabile (und hoffentlich eindeutige) Guid den Metadaten zuweisen
				if( result.FileVersion < new Version( 3, 1, 0, 0 ) )
					result.Guid = MeshModelGuidGenerator.GenerateGuids( zipFile.Entries );

				return result;
			}
		}

		/// <summary>
		/// Serializes the data of this instance and writes it into the specified <paramref name="stream" />.
		/// </summary>
		/// <param name="stream">The stream.</param>
		/// <param name="upgradeVersionNumber">if set to <c>true</c>, the version number is adjusted to match the current version.</param>
		public void WriteTo(Stream stream, bool upgradeVersionNumber)
		{
			if (upgradeVersionNumber)
				FileVersion = MeshModel.MeshModelFileVersion;

			var settings = new XmlWriterSettings
			{
				Indent = false,
				Encoding = System.Text.Encoding.UTF8,
				CloseOutput = false
			};

			using (var writer = XmlWriter.Create(stream, settings))
			{
				writer.WriteStartDocument(true);
				writer.WriteStartElement("MeshModelMetadata");

				writer.WriteElementString("FileVersion", FileVersion.ToString());
				writer.WriteElementString("SourceFormat", SourceFormat);
				writer.WriteElementString("Guid", Guid.ToString("N", System.Globalization.CultureInfo.InvariantCulture));
				writer.WriteElementString("TriangulationHash", TriangulationHash.ToString("N", System.Globalization.CultureInfo.InvariantCulture));
				writer.WriteElementString("Name", Name);
				writer.WriteElementString("PartCount", PartCount.ToString(System.Globalization.CultureInfo.InvariantCulture));

				if (Layer != null && Layer.Length > 0)
				{
					writer.WriteStartElement("Layer");
					foreach (var layer in Layer)
					{
						writer.WriteElementString("string", layer);
					}
					writer.WriteEndElement();
				}
				if (SourceModels != null && SourceModels.Length > 0)
				{
					writer.WriteStartElement("SourceModels");
					foreach (var layer in SourceModels)
					{
						writer.WriteElementString("Model", layer);
					}
					writer.WriteEndElement();
				}
				if (MeshValueEntries != null && MeshValueEntries.Length > 0)
				{
					WriteMeshValueEntries(writer);
				}
				writer.WriteEndElement();
				writer.WriteEndDocument();
			}
		}

		private void WriteMeshValueEntries(XmlWriter writer)
		{
			writer.WriteStartElement("MeshValueEntries");
			foreach (var entry in MeshValueEntries)
			{
				writer.WriteStartElement("MeshValueEntry");
				entry.Write(writer);
				writer.WriteEndElement();
			}
			writer.WriteEndElement();
		}

		/// <summary>
		/// Initializes this instance with the serialized metadata from the specified <paramref name="stream"/>.
		/// </summary>
		public static MeshModelMetadata ReadFrom(Stream stream)
		{
			var settings = new XmlReaderSettings
			{
				IgnoreComments = true,
				IgnoreWhitespace = true,
				IgnoreProcessingInstructions = true,
				CloseInput = false,
				NameTable = new NameTable()
			};
			using (var reader = XmlReader.Create(stream, settings))
			{
				var result = new MeshModelMetadata();
				reader.MoveToElement();
				while (reader.Read())
				{
					switch (reader.Name)
					{
						case "Guid":
							result.Guid = Guid.ParseExact(reader.ReadString(), "N");
							break;
						case "TriangulationHash":
							result.TriangulationHash = Guid.ParseExact(reader.ReadString(), "N");
							break;
						case "Name":
							result.Name = reader.ReadString();
							break;
						case "PartCount":
							result.PartCount = int.Parse(reader.ReadString(), System.Globalization.CultureInfo.InvariantCulture);
							break;
						case "FileVersion":
							result.FileVersion = new Version(reader.ReadString());
							break;
						case "SourceFormat":
							result.SourceFormat = reader.ReadString();
							break;
						case "Layer":
							var layer = new List<string>();
							while (reader.Read() && reader.NodeType != XmlNodeType.EndElement)
							{
								layer.Add(reader.ReadString());
							}
							result.Layer = layer.ToArray();
							break;
						case "SourceModels":
							var sourceModels = new List<string>();
							while (reader.Read() && reader.NodeType != XmlNodeType.EndElement)
							{
								sourceModels.Add(reader.ReadString());
							}
							result.SourceModels = sourceModels.ToArray();
							break;
						case "MeshValueEntries":
							var entries = new List<MeshValueEntry>();
							while (reader.Read() && reader.NodeType != XmlNodeType.EndElement)
							{
								if (reader.Name == "MeshValueEntry")
									entries.Add(MeshValueEntry.Read(reader));
							}
							result.MeshValueEntries = entries.ToArray();
							break;
					}
				}
				return result;
			}
		}

		/// <summary>
		/// Returns a string that represents the current object.
		/// </summary>
		public override string ToString()
		{
			return $"{Name} (version {FileVersion}, format {SourceFormat})";
		}

		#endregion
	}
}