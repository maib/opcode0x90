﻿Imports System.IO
Imports System.Net
Imports System.Net.Sockets
Imports System.Threading

Public Class IRC
    'IRC main thread
    Private IrcThread As Thread

    'Socket and streams
    Private Socket As TcpClient
    Private Stream As NetworkStream
    Private Reader As StreamReader
    Private Writer As StreamWriter

    'IRC network variables
    Private Network As String       'Currently connected IRC network
    Private UserMask As String      'Our own mask
    Private Nick As String          'Our nickname

    'IRC Events
    Public Event OnConnect(ByVal Server As String)
    Public Event OnDisconnect()

    Public Event OnKick(ByVal Nick As String, ByVal Channel As String, ByVal Reason As String)
    Public Event OnCTCP(ByVal Nick As String, ByVal CTCP As String, ByVal Params As String)

    Public Event OnChannelJoin(ByVal Channel As String)
    Public Event OnChannelPart(ByVal Channel As String)
    Public Event OnChannelKick(ByVal Channel As String, ByVal Nick As String, ByVal NickKicked As String, ByVal Reason As String)
    Public Event OnChannelMessage(ByVal Channel As String, ByVal Nick As String, ByVal Message As String)
    Public Event OnChannelUserJoin(ByVal Channel As String, ByVal Nick As String)
    Public Event OnChannelUserPart(ByVal Channel As String, ByVal Nick As String, ByVal Message As String)

    Public Event OnRawMessage(ByVal Message As String)
    Public Event OnRawServerAnnounce(ByVal FullHeader As String, ByVal Header As String(), ByVal Message As String)
    Public Event OnRawUserMessage(ByVal FullHeader As String, ByVal Header As String(), ByVal Message As String)

    Public Event OnException(ByVal ex As Exception)

    Private Sub IrcThreadStart()

        Dim Buffer As String = String.Empty

        Dim Header As String()
        Dim FullHeader As String
        Dim Message As String
        Dim Pos As Integer

        'The main IRC thread
        While Socket.Connected
            Try
                'Poll for message from the network
                Buffer = Reader.ReadLine()
                While (Not String.IsNullOrEmpty(buffer))
                    'Handle all the events happening on the network
                    RaiseEvent OnRawMessage(Buffer)

                    'Debug
                    Debug.Print(buffer)

                    If buffer.StartsWith(":") Then
                        'Extract the message header and body
                        Pos = Buffer.IndexOf(":", 1)
                        FullHeader = Buffer.Substring(1, IIf(Pos > 0, Pos, Buffer.Length - 1))
                        Header = FullHeader.Split(" ")
                        Message = Buffer.Substring(FullHeader.Length + 1)

                        'Is this the NOTICE AUTH announce ?
                        If FullHeader.Contains("NOTICE AUTH") Then
                            'This is the network we are connected to
                            Me.Network = Header(0)
                        End If

                        'Is this a server announce ?
                        If (header(0) = Me.Network) Then
                            'Parse the server announce
                            ParseServerAnnounce(FullHeader, Header, Message)
                            RaiseEvent OnRawServerAnnounce(FullHeader, Header, Message)
                        Else
                            'Parse user message
                            ParseUserMessage(FullHeader, Header, Message)
                            RaiseEvent OnRawUserMessage(FullHeader, Header, Message)
                        End If
                    Else
                        'Extract the message header and body
                        Pos = Buffer.IndexOf(":", 1)
                        Header = Buffer.Substring(0, IIf(Pos > 0, Pos, Buffer.Length)).Split(" ")
                        Message = Buffer.Substring(String.Join(" ", Header).Length + 1)

                        'Parse the message
                        Select Case header(0)
                            Case "PING"
                                'Pong
                                Me.Send("PONG :" + Message)
                        End Select

                    End If

                    'Read from the stream
                    buffer = Reader.ReadLine()
                End While

                'Sleep
                Thread.Sleep(1000)

            Catch ex As IOException
                'Probably disconnected from the network

            Catch ex As Exception
                'Exception occurred
                RaiseEvent OnException(ex)

            End Try

        End While

        'Close all stream
        Writer.Close()
        Reader.Close()
        Stream.Close()

        'Disconnected
        RaiseEvent OnDisconnect()

    End Sub

    Private Sub ParseServerAnnounce(ByVal FullHeader As String, ByVal Header As String(), ByVal Message As String)
        'Parse the server announce
        Select Case Header(1)
            Case "001"
                'Ask for our user mask
                Me.Send("WHO " + Me.Nick)

                'Connected
                RaiseEvent OnConnect(Header(0))
            Case "352"
                'Our user mask
                Me.UserMask = (Header(2) + "!" + Header(4) + "@" + Header(5))
        End Select

    End Sub

    Private Sub ParseUserMessage(ByVal FullHeader As String, ByVal Header As String(), ByVal Message As String)

        Dim Pos As Integer

        'Parse user message
        Select Case Header(1).ToUpper
            Case "PRIVMSG"
                'Is this a CTCP ?
                If Message.StartsWith(Chr(1)) And Message.EndsWith(Chr(1)) Then
                    'CTCP
                    Pos = Message.IndexOf(" ")
                    RaiseEvent OnCTCP(Header(0), Message.Substring(1, IIf(Pos > 0, Pos - 1, Message.Length - 2)), IIf(Pos > 0, Message.Substring(Pos + 1, Message.Length - Pos - 2), String.Empty))
                Else
                    'Channel message
                    RaiseEvent OnChannelMessage(Header(2), Header(0), Message)
                End If

            Case "JOIN"
                If (Header(0) = Me.UserMask) Then
                    'We joined the channel :O
                    RaiseEvent OnChannelJoin(Message)
                Else
                    'User join
                    RaiseEvent OnChannelUserJoin(Message, Header(0))
                End If

            Case "PART"
                If (Header(0) = Me.UserMask) Then
                    'We left the channel :(
                    RaiseEvent OnChannelPart(Header(2))
                Else
                    'User leave
                    RaiseEvent OnChannelUserPart(Header(2), Header(0), Message)
                End If

            Case "KICK"
                If (Header(3) = Me.Nick) Then
                    'Our ass is kicked >:|
                    RaiseEvent OnKick(Header(0), Header(2), Message)
                Else
                    'Someone else is kicked :]
                    RaiseEvent OnChannelKick(Header(2), Header(0), Header(3), Message)
                End If

        End Select

    End Sub

    Public Sub Connect(ByVal Server As String, ByVal Port As Integer, ByVal Nick As String, ByVal User As String)

        'Abort if already connected
        If (Not IrcThread Is Nothing) Then If (IrcThread.IsAlive) Then Exit Sub

        Try
            'Connect to the specified network
            Socket = New TcpClient(Server, Port)
            Stream = Socket.GetStream()

            'Initialize all the stream reader and writer
            Reader = New StreamReader(Stream)
            Writer = New StreamWriter(Stream)

            'Save the info
            Me.Nick = Nick

            'Initiate the IRC session
            Me.Send("NICK " + Nick)                                             'Assign our nickname
            Me.Send("USER " + Nick + " " + Nick + " " + Server + " :" + User)   'Register our user string

            'Start the bot thread
            IrcThread = New Thread(New ThreadStart(AddressOf Me.IrcThreadStart))
            IrcThread.Start()

        Catch ex As Exception
            'Exception occurred
            RaiseEvent OnException(ex)
        End Try

    End Sub

    Public Sub Disconnect()

        If Not IrcThread Is Nothing Then
            If IrcThread.IsAlive Then
                'Disconnect from the network
                Socket.Close()

                'Wait for the thread to die
                While IrcThread.IsAlive
                    Application.DoEvents()
                End While
            End If
        End If

    End Sub

    Public Sub Send(ByVal Data As String)
        Writer.WriteLine(Data)
        Writer.Flush()

        'Debug
        Debug.Print(Data + vbCrLf)
    End Sub

    Public Sub Message(ByVal Target As String, ByVal Message As String)
        'Say something
        Me.Send("PRIVMSG " + Target + " :" + Message)
    End Sub

    Public Sub Notify(ByVal Target As String, ByVal Message As String)
        'Send a notice
        Me.Send("NOTICE " + Target + " :" + Message)
    End Sub

    Public Sub Identify(ByVal Password As String)
        'Identify
        Me.Message("NICKSERV", "IDENTIFY " + Password)
    End Sub

    Public Sub ChannelJoin(ByVal Channel As String)
        'Join the specified channel
        Me.Send("JOIN " + Channel)
    End Sub

    Public Sub ChannelPart(ByVal Channel As String, Optional ByVal Reason As String = "")
        'Join the specified channel
        Me.Send("PART " + Channel + IIf(Not String.IsNullOrEmpty(Reason), (" " + Reason), String.Empty))
    End Sub

End Class
