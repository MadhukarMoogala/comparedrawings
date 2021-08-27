let forgeOAuth = {
  access_token: "",
  logIn: function () {
    let clientId = FORGE_CLIENT_ID;
    let scopes = FORGE_TOKEN_SCOPE;
    let redirectUri = encodeURI(FORGE_REDIRECT_URI);
    $("a").attr(
      "href",
      `${FORGE_AUTHORIZE_URL}` +
        `?response_type=token&client_id=${clientId}&redirect_uri=${redirectUri}&scope=${scopes}`
    );
  },
  userInfo: function () {
    var url = new URL(window.location.href.replace("#", "?"));
    var query_string = url.search;
    var search_params = new URLSearchParams(query_string);
    this.access_token = search_params.get("access_token");
    if (this.access_token) {
      window.localStorage.clear();
      window.localStorage.setItem("forge_token", this.access_token);
    }
    forge_token =
      this.access_token || window.localStorage.getItem("forge_token");
    if (forge_token) {
      fetch(`${FORGE_USER_URL}`, {
        headers: {
          Authorization: `Bearer ${forge_token}`,
        },
      })
        .then((res) => res.text())
        .then((data) => {
          let json = JSON.parse(data);
          let pretty = `${json.userId}\n${json.emailId}\n${json.firstName} ${json.lastName}`;
          $("#userInfo").val(pretty);
        });
    }
  },
};
var forge_token;
//connect websocket
/*async function connect(onmessage) {
  return new Promise((resolve, reject) => {
    const ws = new WebSocket("wss://websockets.forgedesignautomation.io");
    ws.onmessage = onmessage;
    ws.onopen = (e) => resolve(ws);
    ws.onerror = (e) => reject(e.data);
  });
}*/

// wrap workitem in a websocket message.
// Note:
// We pass the access_token as part of the message because
// the connect request has no way to specify headers in a browser.
function generateMessage(workitem) {
  forge_token = forge_token || window.localStorage.getItem("forge_token");
  const message = {
    action: "post-workitem",
    headers: {
      Authorization: `Bearer ${forge_token}`,
    },
    data: workitem,
  };
  console.log(message);
  return JSON.stringify(message);
}
// print incoming and outgoing messages
function printMessage(messages) {
  messages = JSON.parse(messages);
  $("#workItem").val(JSON.stringify(messages, null, "\t"));
}
let ws; //websocket client that we retain between button clicks
let workItem;
async function prepareWorkItem() {
  let res = await getUploadDrawingLink().catch((e) => {
    console.error(e);
  });
  if (res) {
    Drawings.push(res.result.link);
  }
  const workItemBody = {
    activityId: `${FORGE_ACTIVITY_ID}`,
    arguments: {
      HostDrawing: {
        url: `${Drawings[0]}`,
      },
      ToCompareWith: {
        url: `${Drawings[1]}`,
      },
      Result: {
        url: `${Drawings[2]}`,
        headers: {
          "Content-Type": "application/octet-stream",
        },
        verb: "post",
      },
    },
    signatures: {
      activityId: `${FORGE_ACTIVITY_SIGN}`,
    },
  };
  workItem = workItemBody;
  $("#workItem").val(JSON.stringify(workItem, null, "\t"));
}

// handle Submit button click
/*async function onSubmitHandler() {
  try {
    //get workitem content

    //generate a message from it
    const msg = generateMessage(workItem);
    if (!ws) {
      // connect to websocket and hook up a printing message handler
      ws = await connect((e) => {
        printMessage(e.data);
      });
      // if the connection is closed (e.g. due to timeout) then make sure we re-init next time
      ws.onclose = (e) => {
        ws = undefined;
      };
    }
    //send websocket message
    ws.send(msg);
    //print outgoing message
    printMessage(msg);
  } catch (e) {
    $("#workItem").val(`${e}\n`);
  }
}*/
ws = new WebSocket("wss://websockets.forgedesignautomation.io");
ws.onmessage = (e) => {
  printMessage(e.data);
};
ws.onerror = (e) => {
  printMessage(e.data);
};
ws.onclose = (e) => {
  ws = undefined;
};
function onSubmitHandler() {
  const msg = generateMessage(workItem);
  ws.send(msg);
}
