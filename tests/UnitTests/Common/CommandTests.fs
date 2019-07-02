﻿namespace Pulsar.Client.UnitTests.Common

open Expecto
open Expecto.Flip
open Pulsar.Client.Common
open Pulsar.Client.Common.Commands
open pulsar.proto
open Pulsar.Client.Internal
open FSharp.UMX
open System
open System.IO
open ProtoBuf
open System.Net
open System.Threading.Tasks
open CRC32

module CommandsTests =    
            
    let private protoDeserialize<'T> (bytes : byte[]) =
        use stream = new MemoryStream(bytes)
        Serializer.Deserialize<'T>(stream)
    
    let deserializeSimpleCommand(bytes : byte[]) =
        use stream = new MemoryStream(bytes)
        use reader = new BinaryReader(stream)
    
        let totalSize = reader.ReadInt32() |> int32FromBigEndian
        let commandSize = reader.ReadInt32() |> int32FromBigEndian
    
        let command =
            reader.ReadBytes(commandSize)
            |> protoDeserialize<BaseCommand>
    
        (totalSize, commandSize, command)

    let deserializePayloadCommand(bytes : byte[]) =
        use stream = new MemoryStream(bytes)
        use reader = new BinaryReader(stream)
    
        let totalSize = reader.ReadInt32() |> int32FromBigEndian
        let commandSize = reader.ReadInt32() |> int32FromBigEndian
    
        let command =
            reader.ReadBytes(commandSize)
            |> protoDeserialize<BaseCommand>

        let magicNumber = reader.ReadInt16() |> int16FromBigEndian
        let crc32 = reader.ReadInt32() |> int32FromBigEndian
        let medataSize = reader.ReadInt32() |> int32FromBigEndian

        let metadata =
            reader.ReadBytes(medataSize)
            |> protoDeserialize<MessageMetadata>

        let payload = reader.ReadBytes(bytes.Length - 8 - commandSize - 10 - medataSize)
    
        (bytes, totalSize, commandSize, command, magicNumber, crc32, medataSize, metadata, payload)

    let serializeDeserializeSimpleCommand (cmd: (MemoryStream -> Task)) = 
        let stream = new MemoryStream()
        (cmd stream).Wait() 
        let commandBytes = stream.ToArray()
        commandBytes |> deserializeSimpleCommand

    let serializeDeserializePayloadCommand (cmd: (MemoryStream -> Task)) = 
        let stream = new MemoryStream()
        (cmd stream).Wait() 
        let commandBytes = stream.ToArray()
        commandBytes |> deserializePayloadCommand

    [<Tests>]
    let tests =

        testList "CommandsTests" [

            test "newPartitionMetadataRequest should return correct frame" {
                let topicName = "test-topic"
                let requestId = %1UL
               
                let totalSize, commandSize, command = 
                    serializeDeserializeSimpleCommand (newPartitionMetadataRequest topicName requestId)

                totalSize |> Expect.equal "" 23
                commandSize |> Expect.equal "" 19
                command.``type``  |> Expect.equal "" CommandType.PartitionedMetadata
                command.partitionMetadata.Topic |> Expect.equal "" topicName
                command.partitionMetadata.RequestId |> Expect.equal "" (uint64(requestId))
            }

            test "newConnect should return correct frame" {
                let clientVersion = "client-version"
                let protocolVersion = ProtocolVersion.V1

                let totalSize, commandSize, command = 
                    serializeDeserializeSimpleCommand (newConnect clientVersion protocolVersion None)

                totalSize |> Expect.equal "" 26
                commandSize |> Expect.equal "" 22
                command.``type``  |> Expect.equal "" CommandType.Connect
                command.Connect.ClientVersion |> Expect.equal "" clientVersion
                command.Connect.ProtocolVersion |> Expect.equal "" ((int) protocolVersion)
            }

            test "newMessage should return correct frame" {
                let producerId: ProducerId =  % 5UL
                let sequenceId: SequenceId =  % 6UL
                let numMessages =  1
                let metadata = MessageMetadata(ProducerName = "TestMe")
                let payload = [| 1uy; 17uy; |]

                let (bytes, totalSize, commandSize, command, magicNumber, crc32, medataSize, resultMetadata, resultPayload) = 
                    serializeDeserializePayloadCommand (newSend producerId sequenceId numMessages metadata payload)
                
                let crcArrayStart = 8 + commandSize + 6
                let crcArray = bytes.AsSpan(crcArrayStart, 4 + medataSize + resultPayload.Length).ToArray()

                let currentCrc32 = CRC32C.Get(uint32 0, crcArray, crcArray.Length) |> int32

                magicNumber |> Expect.equal "" (int16 0x0e01)
                crc32 |> Expect.equal "" currentCrc32
            }

            test "newProducer should return correct frame" {
                let topicName = "test-topic"
                let producerName = "test-producer"
                let producerId = %1UL
                let requestId = %1UL
               
                let totalSize, commandSize, command = 
                    serializeDeserializeSimpleCommand (newProducer topicName producerName producerId requestId)

                totalSize |> Expect.equal "" 39
                commandSize |> Expect.equal "" 35
                command.``type``  |> Expect.equal "" CommandType.Producer
                command.Producer.Topic |> Expect.equal "" topicName
                command.Producer.RequestId |> Expect.equal "" %requestId
                command.Producer.ProducerId |> Expect.equal "" %producerId
                command.Producer.ProducerName |> Expect.equal "" %producerName
            }

            test "newLookup should return correct frame" {
                let topicName = "test-topic"
                let requestId = %1UL
                let authoritative = true

                let totalSize, commandSize, command = 
                    serializeDeserializeSimpleCommand (newLookup topicName requestId authoritative )

                totalSize |> Expect.equal "" 25
                commandSize |> Expect.equal "" 21
                command.``type``  |> Expect.equal "" CommandType.Lookup
                command.lookupTopic.Topic |> Expect.equal "" topicName
                command.lookupTopic.RequestId |> Expect.equal "" (uint64(requestId))
                command.lookupTopic.Authoritative |> Expect.equal "" authoritative
            }

            test "newGetTopicsOfNamespaceRequest should return correct frame" {
                let ns = NamespaceName("public/default")
                let requestId = %1UL
                let mode = TopicDomain.Persistent

                let totalSize, commandSize, command = 
                    serializeDeserializeSimpleCommand (newGetTopicsOfNamespaceRequest ns requestId mode )

                totalSize |> Expect.equal "" 29
                commandSize |> Expect.equal "" 25
                command.``type``  |> Expect.equal "" CommandType.GetTopicsOfNamespace
                command.getTopicsOfNamespace.Namespace |> Expect.equal "" (ns.ToString())
                command.getTopicsOfNamespace.RequestId |> Expect.equal "" (uint64(requestId))
                command.getTopicsOfNamespace.mode |> Expect.equal "" CommandGetTopicsOfNamespace.Mode.Persistent
            }
        ]