﻿using System.Diagnostics;
using ManicDigger.ClientNative;

//The main function for loading, unloading and sending chunks to players.
public class ServerSystemNotifyMap : ServerSystem
{
    public override void Update(Server server, float dt)
    {
        Stopwatch s = new Stopwatch();
        s.Start();
        int areasizechunks = server.playerareasize / Server.chunksize;
        int areasizeZchunks = server.d_Map.MapSizeZ / Server.chunksize;
        int mapsizeXchunks = server.d_Map.MapSizeX / Server.chunksize;
        int mapsizeYchunks = server.d_Map.MapSizeY / Server.chunksize;
        int mapsizeZchunks = server.d_Map.MapSizeZ / Server.chunksize;
        int[] retNearest = new int[3];
        bool loaded = true;
        while ((s.ElapsedMilliseconds < 10) && (loaded))
        {
            loaded = false;
            foreach (var k in server.clients)
            {
                if (k.Value.state == ClientStateOnServer.Connecting)
                {
                    continue;
                }
                Vector3i playerpos = server.PlayerBlockPosition(k.Value);

                NearestDirty(server, k.Key, playerpos.x, playerpos.y, playerpos.z, retNearest);

                if (retNearest[0] != -1)
                {
                    LoadAndSendChunk(server, k.Value, k.Key, retNearest[0], retNearest[1], retNearest[2], s);
                    loaded = true;
                }
            }
        }
    }

    const int intMaxValue = 2147483647;
    void NearestDirty(Server server, int clientid, int playerx, int playery, int playerz, int[] retNearest)
    {
        int nearestdist = intMaxValue;
        retNearest[0] = -1;
        retNearest[1] = -1;
        retNearest[2] = -1;
        int px = (int)(playerx) / Server.chunksize;
        int py = (int)(playery) / Server.chunksize;
        int pz = (int)(playerz) / Server.chunksize;

        int chunksxy = mapAreaSize(server) / Server.chunksize / 2;
        int chunksz = mapAreaSizeZ(server) / Server.chunksize / 2;

        int startx = px - chunksxy;
        int endx = px + chunksxy;
        int starty = py - chunksxy;
        int endy = py + chunksxy;
        int startz = pz - chunksz;
        int endz = pz + chunksz;

        if (startx < 0) { startx = 0; }
        if (starty < 0) { starty = 0; }
        if (startz < 0) { startz = 0; }
        if (endx >= server.mapsizexchunks()) { endx = server.mapsizexchunks() - 1; }
        if (endy >= server.mapsizeychunks()) { endy = server.mapsizeychunks() - 1; }
        if (endz >= server.mapsizezchunks()) { endz = server.mapsizezchunks() - 1; }

        ClientOnServer client = server.clients[clientid];
        for (int x = startx; x <= endx; x++)
        {
            for (int y = starty; y <= endy; y++)
            {
                for (int z = startz; z <= endz; z++)
                {
                    int pos = MapUtilCi.Index3d(x, y, z, server.d_Map.MapSizeX / Server.chunksize, server.d_Map.MapSizeY / Server.chunksize);
                    if (client.chunksseen[pos])
                    {
                        continue;
                    }
                    {
                        int dx = px - x;
                        int dy = py - y;
                        int dz = pz - z;
                        int dist = dx * dx + dy * dy + dz * dz;
                        if (dist < nearestdist)
                        {
                            nearestdist = dist;
                            retNearest[0] = x;
                            retNearest[1] = y;
                            retNearest[2] = z;
                        }
                    }
                }
            }
        }
    }

    void LoadAndSendChunk(Server server, ClientOnServer c, int kKey, int vx, int vy, int vz, Stopwatch s)
    {
        //load
        server.LoadChunk(vx, vy, vz);
        //send
        int pos = MapUtilCi.Index3d(vx, vy, vz, server.d_Map.MapSizeX / Server.chunksize, server.d_Map.MapSizeY / Server.chunksize);
        if (!c.chunksseen[pos])
        {
            SendChunk(server, kKey, new Vector3i(vx * Server.chunksize, vy * Server.chunksize, vz * Server.chunksize));
        }
    }

    public void SendChunk(Server server, int clientid, Vector3i v)
    {
        ClientOnServer c = server.clients[clientid];
        ServerChunk chunk = server.d_Map.GetChunk(v.x, v.y, v.z);
        server.ClientSeenChunkSet(clientid, v.x, v.y, v.z, (int)server.simulationcurrentframe);
        //sent++;
        byte[] compressedchunk;
        if (MapUtil.IsSolidChunk(chunk.data) && chunk.data[0] == 0)
        {
            //don't send empty chunk.
            compressedchunk = null;
        }
        else
        {
            compressedchunk = server.CompressChunkNetwork(chunk.data);
            //todo!
            //commented because it was being sent too early, before full column was generated.
            //if (!c.heightmapchunksseen.ContainsKey(new Vector2i(v.x, v.y)))
            {
                byte[] heightmapchunk = Misc.UshortArrayToByteArray(server.d_Map.GetHeightmapChunk(v.x, v.y));
                byte[] compressedHeightmapChunk = server.d_NetworkCompression.Compress(heightmapchunk);
                Packet_ServerHeightmapChunk p1 = new Packet_ServerHeightmapChunk()
                {
                    X = v.x,
                    Y = v.y,
                    SizeX = Server.chunksize,
                    SizeY = Server.chunksize,
                    CompressedHeightmap = compressedHeightmapChunk,
                };
                server.SendPacket(clientid, server.Serialize(new Packet_Server() { Id = Packet_ServerIdEnum.HeightmapChunk, HeightmapChunk = p1 }));
                c.heightmapchunksseen[new Vector2i(v.x, v.y)] = (int)server.simulationcurrentframe;
            }
        }
        if (compressedchunk != null)
        {
            foreach (byte[] part in Server.Parts(compressedchunk, 1024))
            {
                Packet_ServerChunkPart p1 = new Packet_ServerChunkPart()
                {
                    CompressedChunkPart = part,
                };
                server.SendPacket(clientid, server.Serialize(new Packet_Server() { Id = Packet_ServerIdEnum.ChunkPart, ChunkPart = p1 }));
            }
        }
        Packet_ServerChunk p = new Packet_ServerChunk()
        {
            X = v.x,
            Y = v.y,
            Z = v.z,
            SizeX = Server.chunksize,
            SizeY = Server.chunksize,
            SizeZ = Server.chunksize,
        };
        server.SendPacket(clientid, server.Serialize(new Packet_Server() { Id = Packet_ServerIdEnum.Chunk_, Chunk_ = p }));
    }

    public int mapAreaSize(Server server) { return server.chunkdrawdistance * Server.chunksize * 2; }
    public int mapAreaSizeZ(Server server) { return mapAreaSize(server); }
}